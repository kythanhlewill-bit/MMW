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
    private readonly IExchangeOrderProviderFactory _orderFactory;
    private readonly ITradeWorkflowService _workflow;
    private readonly IUnitOfWork _unitOfWork;

    public TradeResultSyncService(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Trade> trades,
        IExchangeAccountProviderFactory providerFactory,
        IExchangeOrderProviderFactory orderFactory,
        ITradeWorkflowService workflow,
        IUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _trades = trades;
        _providerFactory = providerFactory;
        _orderFactory = orderFactory;
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

        // Vị thế còn mở thực tế trên sàn → để KHÔNG đóng nhầm lệnh fuzzy/import khi position vẫn còn.
        // null = không đọc được (best-effort, không chặn đóng).
        var openPositionKeys = await TryGetOpenPositionKeysAsync(account, cancellationToken);

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
                        if (TryMatchAndClose(trade, fills, account, openPositionKeys))
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

    /// <summary>
    /// Đọc vị thế còn mở trên sàn, trả set khoá "SYMBOL|Direction" (vd "BTCUSDT|Long").
    /// Null nếu không đọc được (không chặn việc đóng — best-effort).
    /// </summary>
    private async Task<HashSet<string>?> TryGetOpenPositionKeysAsync(TradingAccount account, CancellationToken ct)
    {
        try
        {
            // Đọc vị thế cùng venue với nguồn fills (real net) để khớp dữ liệu.
            var orderProvider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, useTestnet: false);
            var positions = await orderProvider.GetOpenPositionsAsync(null, ct);
            return positions
                .Where(p => p.PositionAmt != 0m)
                .Select(p => PositionKey(p.Symbol, p.IsLong ? TradeDirection.Long : TradeDirection.Short))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    private static string PositionKey(string symbol, TradeDirection direction) => $"{symbol}|{direction}";

    private static bool TryMatchAndClose(
        Trade trade,
        IReadOnlyList<MarketData.Models.ExchangeTrade> fills,
        TradingAccount account,
        HashSet<string>? openPositionKeys)
    {
        // An toàn: nếu vị thế cùng symbol+hướng VẪN còn mở trên sàn thì chưa đóng (tránh đóng nhầm
        // do fuzzy match khớp fill cũ). Chỉ áp dụng khi đọc được vị thế.
        if (openPositionKeys is not null && openPositionKeys.Contains(PositionKey(trade.Symbol, trade.Direction)))
            return false;

        // Phía đóng lệnh: Long đóng bằng SELL (isBuyer=false), Short đóng bằng BUY (isBuyer=true).
        var isClosingSide = trade.Direction != TradeDirection.Long;
        var afterOpen = trade.OpenedAt ?? trade.CreatedDate;

        List<MarketData.Models.ExchangeTrade> closingFills;

        if (!string.IsNullOrWhiteSpace(trade.ExchangeOrderId))
        {
            // Tier 1 — Exact match: lệnh đặt qua MMW có orderId → match chính xác 100%.
            // Lấy fills có cùng orderId (Binance gom partial fills vào cùng một order).
            closingFills = fills
                .Where(f => f.OrderId == trade.ExchangeOrderId)
                .OrderBy(f => f.Time)
                .ToList();
        }
        else
        {
            // Tier 2 — Fuzzy match: import thủ công / không có orderId.
            // Điều kiện: đúng phía đóng + sau thời điểm mở + giá fill nằm trong ±5% entry
            // (loại fills của các lệnh khác trên cùng symbol).
            var entryRef = trade.EntryPrice;
            closingFills = fills
                .Where(f => f.IsBuyer == isClosingSide
                         && f.Time > afterOpen
                         && entryRef > 0m
                         && Math.Abs(f.Price - entryRef) / entryRef <= 0.05m)
                .OrderBy(f => f.Time)
                .ToList();
        }

        if (closingFills.Count == 0) return false;

        // Gom fills cho đến khi đủ quantity.
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

        // Chỉ đóng nếu đã fill >= 90% quantity (cho phép sai số rounding sàn).
        if (filledQty < trade.Quantity * 0.9m) return false;

        trade.ExitPrice = filledQty > 0 ? Math.Round(weightedExitPrice / filledQty, 8) : null;
        trade.RealizedPnl = Math.Round(totalPnl - totalFee - trade.Fee, 8);
        trade.Fee += totalFee;
        trade.ClosedAt = lastFillTime;
        trade.Status = TradeStatus.Closed;
        trade.Outcome = trade.RealizedPnl > 0 ? TradeOutcome.Win
            : trade.RealizedPnl < 0 ? TradeOutcome.Loss
            : TradeOutcome.BreakEven;

        account.CurrentBalance += trade.RealizedPnl.Value;

        return true;
    }
}
