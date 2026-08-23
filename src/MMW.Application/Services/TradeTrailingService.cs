using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Trông chừng những lệnh có phần runner: chốt phần đầu xong thì kéo dừng lỗ về hoà vốn, rồi
/// kéo tiếp theo từng điểm xoay khung lớn.
/// </summary>
/// <remarks>
/// <para><b>Vì sao phải có một dịch vụ riêng.</b> Sàn không biết gì về "runner". Nó chỉ biết
/// các lệnh điều kiện đang treo, và mọi lệnh treo đều đứng yên tại mức đã đặt. Muốn dừng lỗ đi
/// theo giá thì phải có ai đó ở phía mình huỷ lệnh cũ và đặt lệnh mới — không có bước đó thì
/// "để lãi chạy" chỉ là một câu nói, còn kết cục thật vẫn là chạm mục tiêu hoặc chạm dừng lỗ
/// gốc, đúng hai khả năng như trước.</para>
///
/// <para><b>Vì sao đọc vị thế trên sàn chứ không đọc lệnh khớp.</b> Cách nhận ra phần đầu đã
/// chốt là khối lượng vị thế giảm đi mà chưa về 0. Đọc trực tiếp như vậy đúng kể cả khi lệnh
/// chốt phần đầu khớp làm nhiều mảnh, khi có ai đó đóng tay một phần, hoặc khi bản ghi phía
/// mình lỡ mất một nhịp — cả ba trường hợp mà việc ghép từng cú khớp đều trả lời sai.</para>
///
/// <para><b>Vì sao dừng lỗ chỉ đi MỘT chiều.</b> Mọi lần kéo đều phải làm vị thế an toàn hơn.
/// Một cái dừng lỗ có thể lùi ra xa chính là một cái dừng lỗ không tồn tại — nó sẽ lùi đúng vào
/// lúc thị trường đang chứng minh mình sai, và đó là lúc lý do để lùi nghe thuyết phục nhất.</para>
/// </remarks>
public interface ITradeTrailingService
{
    /// <summary>Quét mọi lệnh đang mở có phần runner. Trả về số lệnh đã cập nhật mức.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class TradeTrailingService : ITradeTrailingService
{
    /// <summary>
    /// Đệm đẩy mức hoà vốn ra khỏi giá vào, tính theo phần trăm giá.
    /// </summary>
    /// <remarks>
    /// Hoà vốn đặt đúng bằng giá vào KHÔNG phải hoà vốn: hai lượt phí taker cộng lại khoảng
    /// 0,10% giá trị, nên chạm đúng giá vào là vẫn lỗ đúng chỗ đó. Đệm này đẩy mức dừng qua bên
    /// kia của phí để "về hoà vốn" đúng nghĩa đen.
    /// </remarks>
    private const decimal BreakevenBufferPercent = 0.12m;

    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<EngineSetting> _engineSettings;
    private readonly IExchangeOrderProviderFactory _orderFactory;
    private readonly IMarketDataProvider _marketData;
    private readonly ISwingDetector _swings;
    private readonly ILiveOrderService _liveOrders;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LiveTradingOptions _options;
    private readonly ILogger<TradeTrailingService> _logger;

    public TradeTrailingService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<EngineSetting> engineSettings,
        IExchangeOrderProviderFactory orderFactory,
        IMarketDataProvider marketData,
        ISwingDetector swings,
        ILiveOrderService liveOrders,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        IOptions<LiveTradingOptions> options,
        ILogger<TradeTrailingService> logger)
    {
        _trades = trades;
        _accounts = accounts;
        _engineSettings = engineSettings;
        _orderFactory = orderFactory;
        _marketData = marketData;
        _swings = swings;
        _liveOrders = liveOrders;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return 0;

        // Chỉ những lệnh THẬT SỰ có gì để quản: có mục tiêu gần chưa chốt, hoặc có kéo dừng lỗ.
        var candidates = await _trades.FindListAsync(t =>
            t.IsLive
            && t.Status == TradeStatus.Open
            && (t.FirstTakeProfit != null || t.TrailPivotBars > 0));

        if (candidates.Count == 0) return 0;

        var updated = 0;

        foreach (var trade in candidates)
        {
            try
            {
                if (await ManageAsync(trade, cancellationToken)) updated++;
            }
            catch (Exception ex)
            {
                // Một lệnh hỏng không được kéo theo các lệnh còn lại: mỗi lệnh là một vị thế
                // riêng và mỗi vị thế không được bảo vệ là một rủi ro riêng.
                _logger.LogError(ex, "Kéo dừng lỗ lệnh #{TradeId} lỗi.", trade.Id);
            }
        }

        return updated;
    }

    private async Task<bool> ManageAsync(Trade trade, CancellationToken ct)
    {
        var account = await _accounts.FindAsync(trade.TradingAccountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
            return false;

        var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);
        var positions = await provider.GetOpenPositionsAsync(trade.Symbol, ct);
        var position = positions.FirstOrDefault(p =>
            trade.Direction == TradeDirection.Long ? p.IsLong : p.IsShort);

        // Không còn vị thế nghĩa là lệnh đã đóng hẳn. Việc ghi kết quả thuộc về dịch vụ đối
        // soát kết quả, không thuộc về đây — chạm vào sẽ thành hai nơi cùng ghi một sự thật.
        if (position is null || position.PositionAmt == 0m) return false;

        var remaining = Math.Abs(position.PositionAmt);
        var isLong = trade.Direction == TradeDirection.Long;

        var newStop = (decimal?)null;
        var reason = "";

        // ── Bước 1: phát hiện phần đầu đã chốt ──
        if (trade.FirstTargetFilledAt is null
            && trade.FirstTakeProfit is not null
            && remaining < trade.Quantity * 0.95m)
        {
            trade.FirstTargetFilledAt = DateTime.UtcNow;

            var buffer = trade.EntryPrice * BreakevenBufferPercent / 100m;
            newStop = isLong ? trade.EntryPrice + buffer : trade.EntryPrice - buffer;
            reason = $"đã chốt phần đầu ({trade.Quantity - remaining:N4}/{trade.Quantity:N4}), kéo về hoà vốn";
        }

        // ── Bước 2: kéo theo điểm xoay khung lớn ──
        if (trade.FirstTargetFilledAt is not null && trade.TrailPivotBars > 0)
        {
            var pivotStop = await PivotStopAsync(trade, account, isLong, ct);

            // Chỉ nhận mức tốt HƠN mức đang có — kể cả mức hoà vốn vừa tính ở bước trên.
            var reference = newStop ?? trade.StopLoss;
            if (pivotStop is { } candidate
                && (reference is null || (isLong ? candidate > reference : candidate < reference)))
            {
                newStop = candidate;
                reason = reason.Length > 0
                    ? $"{reason}; rồi kéo tiếp theo điểm xoay khung lớn"
                    : "kéo theo điểm xoay khung lớn";
            }
        }

        if (newStop is not { } stop) return false;

        // Dừng lỗ không được nằm sai phía giá hiện tại — đặt vậy sàn sẽ kích hoạt ngay lập tức
        // và biến một cú kéo thành một cú đóng lệnh bằng giá thị trường.
        var mark = position.EntryPrice > 0m ? position.EntryPrice : trade.EntryPrice;
        var last = await LastPriceAsync(trade.Symbol, ct) ?? mark;
        if (isLong ? stop >= last : stop <= last)
        {
            _logger.LogInformation(
                "Lệnh #{TradeId}: bỏ qua kéo dừng lỗ tới {Stop} vì giá hiện tại {Last} đã ở sai phía.",
                trade.Id, stop, last);
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(ct);
            return false;
        }

        var previous = trade.StopLoss;
        trade.StopLoss = stop;
        trade.TrailUpdateCount++;
        trade.LiveNote = Truncate(
            $"Kéo dừng lỗ {previous?.ToString() ?? "—"} → {stop} lúc {DateTime.UtcNow:HH:mm:ss} UTC ({reason}).", 500);
        _trades.Update(trade);
        await _unitOfWork.CommitAsync(ct);

        // Đặt lại trọn bộ lệnh bảo vệ theo mức mới. Đi qua LiveOrderService để phần huỷ lệnh cũ,
        // retry và ghi trạng thái dùng chung một đường với mọi chỗ khác.
        await _liveOrders.SyncLevelsAsync(trade.Id, ct);

        try
        {
            await _notifications.PublishAsync(new NotificationCreateModel
            {
                Type = NotificationType.TradeRiskWarning,
                Severity = NotificationSeverity.Info,
                Title = $"Kéo dừng lỗ #{trade.Id} · {trade.Symbol}",
                Message = $"{previous?.ToString() ?? "—"} → {stop} ({reason}).",
                Source = "trade_trailing",
                SourceKey = $"{account.Id}:{trade.Id}:{trade.TrailUpdateCount}",
                RelatedSymbol = trade.Symbol,
                RelatedUrl = "/Trades",
                ExpiresAt = DateTime.UtcNow.AddHours(12),
            }, ct);
        }
        catch (Exception ex)
        {
            // Báo tin hỏng không được làm hỏng việc kéo dừng lỗ — mức mới đã nằm trên sàn rồi.
            _logger.LogWarning(ex, "Không gửi được thông báo kéo dừng lỗ lệnh #{TradeId}.", trade.Id);
        }

        _logger.LogInformation("Lệnh #{TradeId}: kéo dừng lỗ {Old} → {New} ({Reason}).",
            trade.Id, previous, stop, reason);
        return true;
    }

    /// <summary>
    /// Mức dừng lỗ suy từ điểm xoay đã xác nhận gần nhất trên khung thiên hướng.
    /// </summary>
    /// <remarks>
    /// Dùng khung THIÊN HƯỚNG chứ không dùng khung vào lệnh, vì đây là bộ luật swing: kéo dừng
    /// lỗ theo điểm xoay 15 phút sẽ bám sát tới mức mọi nhịp thở trong phiên đều đẩy được ta ra
    /// khỏi lệnh, và như thế thì phần runner không bao giờ sống nổi tới mục tiêu 4h. Kéo theo
    /// khung lớn nghĩa là chấp nhận trả lại một phần lãi đang có để đổi lấy chỗ cho lệnh thở.
    /// </remarks>
    private async Task<decimal?> PivotStopAsync(Trade trade, TradingAccount account, bool isLong, CancellationToken ct)
    {
        var setting = await _engineSettings.FirstOrDefaultAsync(e => e.TradingAccountId == account.Id);
        var timeframe = setting?.BiasTimeframe ?? "4h";

        var candles = await _marketData.GetCandlesAsync(trade.Symbol, timeframe, 120, ct);
        if (candles.Count == 0) return null;

        var pivots = _swings.Detect(candles, trade.TrailPivotBars);
        var level = isLong
            ? pivots.Where(p => !p.IsHigh).Select(p => (decimal?)p.Price).LastOrDefault()
            : pivots.Where(p => p.IsHigh).Select(p => (decimal?)p.Price).LastOrDefault();

        if (level is not { } lvl || lvl <= 0m) return null;

        // Đệm dưới điểm xoay, cùng đơn vị với chính khung đã sinh ra nó.
        var buffer = lvl * (setting?.V7StopBufferAtr ?? 0.25m) / 100m;
        return isLong ? lvl - buffer : lvl + buffer;
    }

    private async Task<decimal?> LastPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var candles = await _marketData.GetCandlesAsync(symbol, "1m", 2, ct);
            return candles.Count == 0 ? null : candles[^1].Close;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không lấy được giá gần nhất của {Symbol}.", symbol);
            return null;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
