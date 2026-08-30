using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public interface ITradeTimeStopService
{
    /// <summary>Đóng các vị thế đã giữ quá hạn theo cấu hình. Trả về số lệnh đã ra lệnh đóng.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dừng theo THỜI GIAN: một vị thế không được sống lâu hơn lập luận đã mở nó.
/// </summary>
/// <remarks>
/// Engine có ba cách thoát lệnh — chạm dừng lỗ, chạm chốt lời, và hết hạn lệnh CHỜ. Không cách
/// nào chạm tới một vị thế đã khớp mà giá cứ đi ngang. Lớp này là cách thứ tư.
///
/// Lý do nó cần tồn tại, đo trên chính sổ lệnh ngày 29–30/08:
/// <code>
/// #57 BTCUSDC mở 26/08, còn mở sau 4 ngày
/// #72 ETHUSDT mở 28/08, còn mở sau 2 ngày
/// ⟹ 476 phiếu được chấm, 0 lệnh vào; 323 phiếu bị veto PositionAlreadyOpen
/// </code>
/// Hai vị thế đứng im đã khoá toàn bộ engine, vì <c>MaxConcurrentPositions</c> là 2 và cổng
/// chống trùng còn chặn cả các mã cùng tài sản gốc. Chi phí cơ hội của việc "cứ để đó" không
/// hiện ra ở đâu trong P&amp;L của hai lệnh ấy, nên nếu không đo thì không ai thấy.
///
/// Hạn tính theo <see cref="TradeStyle"/>, không phải một con số chung: lệnh trong phiên chấm
/// trên nến 15 phút và lệnh swing chấm trên cấu trúc 4 giờ, nên "quá hạn" với cái này là bình
/// thường với cái kia.
///
/// Đóng qua <see cref="ILiveOrderService.CloseOnExchangeAsync"/> và KHÔNG tự ghi kết quả vào sổ:
/// <see cref="ITradeResultSyncService"/> vẫn là nơi duy nhất ghi giá thoát và lãi/lỗ. Hai nơi
/// cùng ghi một sự thật là cách chắc chắn nhất để có hai phiên bản của nó.
/// </remarks>
public sealed class TradeTimeStopService : ITradeTimeStopService
{
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly ILiveOrderService _liveOrders;
    private readonly INotificationService _notifications;
    private readonly LiveTradingOptions _options;
    private readonly ILogger<TradeTimeStopService> _logger;

    public TradeTimeStopService(
        IBaseRepository<Trade> trades,
        IBaseRepository<EngineSetting> settings,
        ILiveOrderService liveOrders,
        INotificationService notifications,
        IOptions<LiveTradingOptions> options,
        ILogger<TradeTimeStopService> logger)
    {
        _trades = trades;
        _settings = settings;
        _liveOrders = liveOrders;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return 0;

        // Chỉ vị thế ĐÃ KHỚP. Lệnh chờ chưa khớp có hạn riêng và đường xử lý riêng trong
        // LiveOrderService.ReconcilePendingEntriesAsync — đụng vào đây sẽ thành hai nơi cùng huỷ
        // một lệnh, và cái thua cuộc sẽ huỷ nhầm một vị thế vừa khớp.
        var open = await _trades.FindListAsync(t =>
            t.IsLive
            && t.Status == TradeStatus.Open
            && t.LiveStatus != LiveOrderStatus.EntryPending
            && t.OpenedAt != null);

        if (open.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var closed = 0;

        foreach (var trade in open)
        {
            var setting = await _settings.FirstOrDefaultAsync(s => s.TradingAccountId == trade.TradingAccountId);
            if (setting is null) continue;

            var limitHours = trade.Style == TradeStyle.HtfSwing
                ? setting.MaxHoldingHoursSwing
                : setting.MaxHoldingHoursIntraday;

            if (limitHours <= 0) continue;

            var age = now - trade.OpenedAt!.Value;
            if (age.TotalHours < limitHours) continue;

            var reason =
                $"Giữ {age.TotalHours:N1} giờ, quá hạn {limitHours} giờ của nhóm {trade.Style} — đóng theo dừng thời gian.";

            try
            {
                await _liveOrders.CloseOnExchangeAsync(trade.Id, cancellationToken);
                closed++;

                _logger.LogInformation("Dừng thời gian: lệnh #{TradeId} {Symbol} — {Reason}",
                    trade.Id, trade.Symbol, reason);

                await _notifications.PublishAsync(new NotificationCreateModel
                {
                    Type = NotificationType.TradeRiskWarning,
                    Severity = NotificationSeverity.Info,
                    Title = $"Dừng thời gian #{trade.Id} · {trade.Symbol} {trade.Direction}",
                    Message = reason,
                    Source = "trade_time_stop",
                    // Khoá theo GIỜ tuổi để một vị thế mà sàn chưa đóng xong không đẻ ra một
                    // thông báo giống hệt ở mỗi vòng job.
                    SourceKey = $"{trade.TradingAccountId}:{trade.Id}:{(int)age.TotalHours}",
                    RelatedSymbol = trade.Symbol,
                    RelatedUrl = "/Trades",
                    ExpiresAt = now.AddHours(12),
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                // Một vị thế đóng hỏng không được chặn các vị thế còn lại. Vòng job sau thử tiếp:
                // điều kiện quá hạn vẫn đúng, nên không cần trạng thái riêng để nhớ việc dở dang.
                _logger.LogError(ex, "Dừng thời gian: đóng lệnh #{TradeId} {Symbol} lỗi.",
                    trade.Id, trade.Symbol);
            }
        }

        return closed;
    }
}
