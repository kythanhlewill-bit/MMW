using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Lấy kết quả lệnh từ Binance (fills) và đồng bộ vào Trade Journal.
/// Cho mỗi tài khoản có API key: lấy fills gần nhất → match với Trade Open → update PnL/Status.
/// </summary>
public class TradeResultSyncService : ITradeResultSyncService
{
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<Trade> _trades;
    private readonly IExchangeAccountProviderFactory _providerFactory;
    private readonly ITradeWorkflowService _workflow;
    private readonly IUnitOfWork _unitOfWork;

    public TradeResultSyncService(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Trade> trades,
        IExchangeAccountProviderFactory providerFactory,
        ITradeWorkflowService workflow,
        IUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _trades = trades;
        _providerFactory = providerFactory;
        _workflow = workflow;
        _unitOfWork = unitOfWork;
    }

    public async Task<SyncResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accounts.FindListAsync(a => a.IsActive && a.ApiKey != null && a.ApiSecret != null);
        var totalSynced = 0;
        var totalFailed = 0;
        var totalSkipped = 0;

        foreach (var account in accounts)
        {
            var result = await SyncAccountAsync(account.Id, cancellationToken);
            totalSynced += result.Synced;
            totalFailed += result.Failed;
            totalSkipped += result.Skipped;
        }

        return new SyncResult(totalSynced, totalFailed, totalSkipped);
    }

    public async Task<SyncResult> SyncAccountAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.FindAsync(accountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
            return new SyncResult(0, 0, 0);

        var openTrades = await _trades.FindListAsync(t =>
            t.TradingAccountId == accountId && t.Status == TradeStatus.Open);

        if (openTrades.Count == 0)
            return new SyncResult(0, 0, 0);

        var provider = _providerFactory.Create(account.ApiKey, account.ApiSecret);
        var synced = 0;
        var failed = 0;
        var skipped = 0;

        var symbolGroups = openTrades.GroupBy(t => t.Symbol);

        foreach (var group in symbolGroups)
        {
            try
            {
                var fills = await provider.GetMyTradesAsync(group.Key, 500, cancellationToken);
                if (fills.Count == 0) { skipped += group.Count(); continue; }

                foreach (var trade in group)
                {
                    try
                    {
                        if (TryMatchAndClose(trade, fills, account))
                        {
                            _trades.Update(trade);
                            synced++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            catch
            {
                failed += group.Count();
            }
        }

        if (synced > 0)
        {
            await _unitOfWork.CommitAsync(cancellationToken);

            foreach (var trade in openTrades.Where(t => t.Status == TradeStatus.Closed))
            {
                try { await _workflow.ProcessTradeAsync(trade.Id); } catch { }
            }
        }

        return new SyncResult(synced, failed, skipped);
    }

    private static bool TryMatchAndClose(Trade trade, IReadOnlyList<MarketData.Models.ExchangeTrade> fills, TradingAccount account)
    {
        // Tìm fills đóng lệnh: BUY lệnh Long → tìm SELL fills sau entry time; ngược lại cho Short.
        var isClosingSide = trade.Direction == TradeDirection.Long ? false : true; // close Long = Sell (isBuyer=false)
        var afterOpen = trade.OpenedAt ?? trade.CreatedDate;

        var closingFills = fills
            .Where(f => f.IsBuyer == isClosingSide && f.Time > afterOpen)
            .OrderBy(f => f.Time)
            .ToList();

        if (closingFills.Count == 0) return false;

        // Ghép fills cho đến đủ quantity hoặc hết.
        var remainingQty = trade.Quantity;
        var totalPnl = 0m;
        var totalFee = 0m;
        var weightedExitPrice = 0m;
        var filledQty = 0m;
        DateTime? lastFillTime = null;

        foreach (var fill in closingFills)
        {
            if (remainingQty <= 0m) break;

            var qty = Math.Min(fill.Quantity, remainingQty);
            filledQty += qty;
            remainingQty -= qty;
            totalFee += fill.Commission;
            weightedExitPrice += fill.Price * qty;
            lastFillTime = fill.Time;

            var pnl = trade.Direction == TradeDirection.Long
                ? (fill.Price - trade.EntryPrice) * qty
                : (trade.EntryPrice - fill.Price) * qty;
            totalPnl += pnl;
        }

        // Chỉ đóng nếu đã fill >= 90% quantity (cho phép sai số rounding).
        if (filledQty < trade.Quantity * 0.9m) return false;

        trade.ExitPrice = filledQty > 0 ? Math.Round(weightedExitPrice / filledQty, 8) : null;
        trade.RealizedPnl = Math.Round(totalPnl - totalFee - trade.Fee, 8);
        trade.Fee += totalFee;
        trade.ClosedAt = lastFillTime;
        trade.Status = TradeStatus.Closed;
        trade.Outcome = trade.RealizedPnl > 0 ? TradeOutcome.Win
            : trade.RealizedPnl < 0 ? TradeOutcome.Loss
            : TradeOutcome.BreakEven;

        // Cập nhật balance tài khoản.
        account.CurrentBalance += trade.RealizedPnl.Value;

        return true;
    }
}
