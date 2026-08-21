using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Models;
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
    private readonly LiveTradingOptions _liveTrading;
    private readonly INotificationService _notifications;

    public TradeResultSyncService(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Trade> trades,
        IExchangeAccountProviderFactory providerFactory,
        IExchangeOrderProviderFactory orderFactory,
        ITradeWorkflowService workflow,
        IUnitOfWork unitOfWork,
        IOptions<LiveTradingOptions> liveTrading,
        INotificationService notifications)
    {
        _accounts = accounts;
        _trades = trades;
        _providerFactory = providerFactory;
        _orderFactory = orderFactory;
        _workflow = workflow;
        _unitOfWork = unitOfWork;
        _liveTrading = liveTrading.Value;
        _notifications = notifications;
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

        var provider = _providerFactory.Create(account.ApiKey, account.ApiSecret, _liveTrading.UseTestnet);
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
                await NotifyClosedAsync(trade, cancellationToken);
            }
        }

        return new SyncResult(synced, failed, skipped);
    }

    /// <summary>Báo lệnh đã đóng, và đóng vì chạm chốt lời hay dừng lỗ.</summary>
    /// <remarks>
    /// Phân loại bằng cách so giá thoát với hai mức, KHÔNG bằng lãi/lỗ: một lệnh chạm chốt lời
    /// vẫn có thể lỗ sau phí, và gọi nó là "dừng lỗ" thì thông báo nói sai chuyện đã xảy ra.
    ///
    /// Chỉ dám khẳng định chạm mức khi giá thoát nằm trong 25% khoảng vào–dừng quanh mức đó.
    /// Ngoài dải ấy thì lệnh đóng vì lý do khác (đóng tay, làm phẳng trước cửa sổ tin, sàn thanh
    /// lý) và thông báo phải nói đúng như vậy thay vì gán bừa cho mức gần nhất.
    ///
    /// Nuốt mọi ngoại lệ: không gửi được thông báo là chuyện nhỏ, còn để nó ném ra sẽ chặn vòng
    /// đồng bộ của những lệnh còn lại — mất dữ liệu thật để đổi lấy một dòng thông báo.
    /// </remarks>
    private async Task NotifyClosedAsync(Trade trade, CancellationToken ct)
    {
        try
        {
            var (label, severity) = ClassifyExit(trade);
            var pnl = trade.RealizedPnl ?? 0m;
            var sign = pnl >= 0m ? "+" : "";

            await _notifications.PublishAsync(new NotificationCreateModel
            {
                Type = NotificationType.TradeRiskWarning,
                Severity = severity,
                Title = $"Đóng lệnh #{trade.Id} · {trade.Symbol} {trade.Direction} · {label}",
                Message = $"Vào {trade.EntryPrice} → ra {trade.ExitPrice?.ToString() ?? "—"} · "
                          + $"lãi/lỗ {sign}{pnl:N2} USDT (đã trừ phí {trade.Fee:N2}) · "
                          + $"dừng lỗ {trade.StopLoss?.ToString() ?? "—"}, "
                          + $"chốt lời {trade.TakeProfit?.ToString() ?? "—"}.",
                Source = "trade_sync",
                SourceKey = $"close:{trade.Id}",
                RelatedSymbol = trade.Symbol,
                RelatedUrl = "/Trades",
                ExpiresAt = DateTime.UtcNow.AddHours(24),
            }, ct);
        }
        catch
        {
            // Xem chú thích trên: thông báo hỏng không được phép làm hỏng việc đồng bộ.
        }
    }

    /// <summary>Giá thoát nằm ở đâu so với hai mức đã đặt.</summary>
    internal static (string Label, NotificationSeverity Severity) ClassifyExit(Trade trade)
    {
        if (trade.ExitPrice is not { } exit) return ("đã đóng", NotificationSeverity.Info);

        // Không có dừng lỗ thì không có thước đo khoảng cách, nên không khẳng định gì cả. Đoán
        // bừa ở đây sẽ sinh ra thông báo "CHẠM DỪNG LỖ" cho một lệnh chưa từng đặt dừng lỗ.
        if (trade.StopLoss is not { } stop) return ("đã đóng", NotificationSeverity.Info);

        var risk = Math.Abs(trade.EntryPrice - stop);
        if (risk <= 0m) return ("đã đóng", NotificationSeverity.Info);

        var tolerance = risk * 0.25m;

        if (Math.Abs(exit - stop) <= tolerance)
            return ("CHẠM DỪNG LỖ", NotificationSeverity.Warning);

        if (trade.TakeProfit is { } target && Math.Abs(exit - target) <= tolerance)
            return ("CHẠM CHỐT LỜI", NotificationSeverity.Info);

        return ("đóng ngoài hai mức", NotificationSeverity.Info);
    }

    /// <summary>
    /// Đọc vị thế còn mở trên sàn, trả set khoá "SYMBOL|Direction" (vd "BTCUSDT|Long").
    /// Null nếu không đọc được (không chặn việc đóng — best-effort).
    /// </summary>
    private async Task<HashSet<string>?> TryGetOpenPositionKeysAsync(TradingAccount account, CancellationToken ct)
    {
        try
        {
            // Phải cùng venue với nguồn fills ở trên, nếu không hai bên nói về hai sàn khác nhau.
            // Trước đây chỗ này ghi cứng false: chạy testnet thì lời gọi trả -2015, hàm nuốt lỗi
            // và trả null, và rào an toàn "vị thế còn mở trên sàn thì chưa đóng" biến mất — nhật ký
            // đóng lệnh theo fuzzy match trong khi vị thế vẫn đang chạy.
            var orderProvider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _liveTrading.UseTestnet);
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

    internal static bool TryMatchAndClose(
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
        var entryOrderId = string.IsNullOrWhiteSpace(trade.ExchangeOrderId) ? null : trade.ExchangeOrderId;

        // ExchangeOrderId là id lệnh VÀO, không phải lệnh ra. Nó dùng để LOẠI TRỪ fill của chính
        // cú vào, chứ không phải để nhận diện cú đóng — SL/TP là hai lệnh khác với id khác, và
        // MMW không lưu id của chúng.
        //
        // Trước đây chỗ này lấy thẳng fills cùng id rồi coi là fill đóng lệnh. Hệ quả: ngay giây
        // lệnh vào khớp, hàm này "đóng" lệnh tại chính giá vào — lãi/lỗ bằng đúng tiền phí, còn
        // vị thế thật vẫn chạy trên sàn mà nhật ký ghi là đã xong.
        var entryFills = entryOrderId is null
            ? []
            : fills.Where(f => f.OrderId == entryOrderId).ToList();

        // Có id lệnh vào mà chưa thấy fill nào của nó nghĩa là lệnh chờ chưa khớp — chưa có vị
        // thế nào để đóng. Không có bước này thì một lệnh chờ treo sẽ bị gán nhầm fill của lệnh
        // trước đó trên cùng mã.
        if (entryOrderId is not null && entryFills.Count == 0) return false;

        var afterOpen = entryFills.Count > 0
            ? entryFills.Max(f => f.Time)
            : trade.OpenedAt ?? trade.CreatedDate;

        var entryRef = trade.EntryPrice;
        var closingFills = fills
            .Where(f => f.IsBuyer == isClosingSide
                     && (entryOrderId is null || f.OrderId != entryOrderId)
                     && f.Time >= afterOpen
                     && entryRef > 0m
                     // Dải ±5% loại fills của các lệnh khác trên cùng mã. Rộng hơn mọi dừng
                     // lỗ/chốt lời mà engine đặt, nhưng vẫn đủ chặt để không nuốt một lệnh tay
                     // ở vùng giá khác.
                     && Math.Abs(f.Price - entryRef) / entryRef <= 0.05m)
            .OrderBy(f => f.Time)
            .ToList();

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
