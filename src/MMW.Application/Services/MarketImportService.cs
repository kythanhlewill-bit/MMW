using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Import fill từ sàn vào journal. Mỗi fill → 1 Trade (Source=Import), chống trùng bằng ExternalId.
/// Hạn chế đã biết: fill chưa được ghép thành lệnh round-trip nên PnL/RR chưa có ý nghĩa
/// tới khi bổ sung bộ dựng vị thế (FIFO) sau này.
/// </summary>
public class MarketImportService : IMarketImportService
{
    private readonly IExchangeAccountProvider _exchange;
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IUnitOfWork _unitOfWork;

    public MarketImportService(
        IExchangeAccountProvider exchange,
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IUnitOfWork unitOfWork)
    {
        _exchange = exchange;
        _trades = trades;
        _accounts = accounts;
        _unitOfWork = unitOfWork;
    }

    public async Task<ImportResult> ImportTradesAsync(long accountId, string symbol, int limit = 500, CancellationToken cancellationToken = default)
    {
        _ = await _accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Không tìm thấy TradingAccount {accountId}.");

        var fills = await _exchange.GetMyTradesAsync(symbol, limit, cancellationToken);
        if (fills.Count == 0)
            return new ImportResult(0, 0);

        // Các ExternalId đã có để chống trùng.
        var existing = (await _trades.FindListAsync(
                t => t.TradingAccountId == accountId && t.ExternalId != null))
            .Select(t => t.ExternalId!)
            .ToHashSet();

        var toAdd = new List<Trade>();
        var skipped = 0;
        foreach (var f in fills)
        {
            if (existing.Contains(f.Id))
            {
                skipped++;
                continue;
            }

            toAdd.Add(new Trade
            {
                TradingAccountId = accountId,
                Symbol = f.Symbol,
                Direction = f.IsBuyer ? TradeDirection.Long : TradeDirection.Short,
                Status = TradeStatus.Closed,
                Source = TradeSource.Import,
                EntryPrice = f.Price,
                Quantity = f.Quantity,
                Fee = f.Commission,
                OpenedAt = f.Time,
                ClosedAt = f.Time,
                ExternalId = f.Id,
            });
            existing.Add(f.Id);
        }

        if (toAdd.Count > 0)
        {
            await _trades.AddRangeAsync(toAdd);
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        return new ImportResult(toAdd.Count, skipped);
    }
}
