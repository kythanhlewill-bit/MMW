using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>Việc phải làm với một vị thế đang mở trước khi cửa sổ chặn bắt đầu.</summary>
public enum PositionActionKind
{
    /// <summary>Đã đủ lãi: kéo dừng lỗ về giá vào lệnh.</summary>
    MoveStopToBreakeven = 1,

    /// <summary>Chưa đủ lãi: đóng bớt một phần khối lượng.</summary>
    ClosePartial = 2,
}

/// <param name="RMultiple">Lãi/lỗ hiện tại tính theo R. Null khi không tính được (thiếu dừng lỗ).</param>
public sealed record PositionAction(
    long TradeId,
    string Symbol,
    PositionActionKind Kind,
    decimal? RMultiple,
    string ReasonVi);

public interface IPositionManageService
{
    /// <summary>
    /// Rà soát vị thế đang mở và quyết định việc phải làm nếu sắp vào cửa sổ chặn (FR-013).
    /// </summary>
    Task<IReadOnlyList<PositionAction>> RunAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

/// <summary>
/// Vị thế đang mở khi bước vào cửa sổ chặn KHÔNG được để trần (FR-013).
/// </summary>
/// <remarks>
/// Nguyên tắc của lớp này: mọi vị thế đang mở đều nhận MỘT hành động, không có nhánh nào để
/// nguyên trạng. "Để nguyên" chính là kịch bản mà cả tầng chặn theo khung giờ sinh ra để tránh —
/// đứng ngoài lúc CPI ra mà vẫn ôm vị thế cũ thì chẳng tránh được gì.
///
/// Thiếu dữ liệu cũng phải hành động, và hành động an toàn hơn: không lấy được giá hay không có
/// dừng lỗ thì đóng bớt, chứ không phải bỏ qua.
///
/// Phạm vi hiện tại: cập nhật nhật ký và phát thông báo cho trader. Gửi lệnh thật lên sàn đi qua
/// <c>ILiveOrderService</c> và vẫn nằm sau cổng <c>LiveTrading.Enabled</c> đang TẮT — lớp này
/// không mở cổng đó.
/// </remarks>
public sealed class PositionManageService : IPositionManageService
{
    private readonly ITimeGuardService _timeGuard;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<Trade> _trades;
    private readonly IMarketDataProvider _marketData;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionManageService> _logger;

    public PositionManageService(
        ITimeGuardService timeGuard,
        IBaseRepository<EngineSetting> settings,
        IBaseRepository<Trade> trades,
        IMarketDataProvider marketData,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        ILogger<PositionManageService> logger)
    {
        _timeGuard = timeGuard;
        _settings = settings;
        _trades = trades;
        _marketData = marketData;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PositionAction>> RunAsync(
        long tradingAccountId, DateTime utcNow, CancellationToken ct = default)
    {
        var setting = await _settings
            .Get(s => s.TradingAccountId == tradingAccountId)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Tài khoản {tradingAccountId} chưa có cấu hình engine (EngineSetting).");

        var upcoming = await _timeGuard.GetUpcomingAsync(
            tradingAccountId, utcNow, setting.BlackoutLeadMinutes, ct);

        if (upcoming is null || !upcoming.RequiresPositionAction)
            return Array.Empty<PositionAction>();

        var open = await _trades
            .Get(t => t.TradingAccountId == tradingAccountId && t.Status == TradeStatus.Open)
            .ToListAsync(ct);

        if (open.Count == 0) return Array.Empty<PositionAction>();

        var actions = new List<PositionAction>(open.Count);
        foreach (var trade in open)
        {
            var action = await DecideAsync(trade, setting, upcoming, ct);
            actions.Add(action);

            if (action.Kind == PositionActionKind.MoveStopToBreakeven)
            {
                trade.StopLoss = trade.EntryPrice;
                _trades.Update(trade);
            }

            _logger.LogInformation(
                "Xử lý vị thế trước cửa sổ chặn. tradeId={TradeId} symbol={Symbol} action={Action} " +
                "rMultiple={RMultiple} windowFromUtc={WindowFromUtc:o} kind={Kind} evaluatedAtUtc={EvaluatedAtUtc:o}",
                trade.Id, trade.Symbol, action.Kind, action.RMultiple,
                upcoming.FromUtc, upcoming.Kind, utcNow);
        }

        await _unitOfWork.CommitAsync(ct);
        await NotifyAsync(tradingAccountId, upcoming, actions, ct);
        await WarnOnClockDriftAsync(open[0].Symbol, setting, utcNow, ct);

        return actions;
    }

    private async Task<PositionAction> DecideAsync(
        Trade trade, EngineSetting setting, BlackoutWindow window, CancellationToken ct)
    {
        var r = await ComputeRMultipleAsync(trade, ct);

        if (r is null)
        {
            return new PositionAction(trade.Id, trade.Symbol, PositionActionKind.ClosePartial, null,
                $"Không tính được lãi theo R (thiếu dừng lỗ hoặc không lấy được giá) — đóng " +
                $"{setting.BlackoutPartialClosePercent:0.#}% trước \"{window.Title}\".");
        }

        if (r.Value >= setting.BlackoutBreakevenAtR)
        {
            return new PositionAction(trade.Id, trade.Symbol, PositionActionKind.MoveStopToBreakeven, r,
                $"Đang lãi {r.Value:0.00}R (≥ {setting.BlackoutBreakevenAtR:0.##}R) — kéo dừng lỗ về " +
                $"hoà vốn trước \"{window.Title}\".");
        }

        return new PositionAction(trade.Id, trade.Symbol, PositionActionKind.ClosePartial, r,
            $"Mới {r.Value:0.00}R (< {setting.BlackoutBreakevenAtR:0.##}R) — đóng " +
            $"{setting.BlackoutPartialClosePercent:0.#}% trước \"{window.Title}\".");
    }

    private async Task<decimal?> ComputeRMultipleAsync(Trade trade, CancellationToken ct)
    {
        if (trade.StopLoss is null) return null;

        var risk = trade.Direction == TradeDirection.Long
            ? trade.EntryPrice - trade.StopLoss.Value
            : trade.StopLoss.Value - trade.EntryPrice;

        if (risk <= 0) return null;   // dừng lỗ đặt sai phía: coi như không tính được

        decimal price;
        try
        {
            price = (await _marketData.GetTickerAsync(trade.Symbol, ct)).Price;
        }
        catch (Exception ex)
        {
            // Sàn lỗi không phải lý do để bỏ mặc vị thế — trả null để rơi vào nhánh đóng bớt.
            _logger.LogWarning(ex, "Không lấy được giá {Symbol} khi xử lý vị thế trước cửa sổ chặn.", trade.Symbol);
            return null;
        }

        if (price <= 0) return null;

        var profit = trade.Direction == TradeDirection.Long
            ? price - trade.EntryPrice
            : trade.EntryPrice - price;

        return profit / risk;
    }

    private async Task NotifyAsync(
        long tradingAccountId, BlackoutWindow window, IReadOnlyList<PositionAction> actions, CancellationToken ct)
    {
        var lines = actions.Select(a => $"• {a.Symbol} (#{a.TradeId}): {a.ReasonVi}");

        await _notifications.PublishAsync(new NotificationCreateModel
        {
            Type = NotificationType.TradeRiskWarning,
            Severity = NotificationSeverity.Warning,
            Title = $"Sắp vào cửa sổ chặn: {window.Title}",
            Message = string.Join(Environment.NewLine, lines),
            Source = nameof(PositionManageService),

            // Một khoá cho mỗi cửa sổ: job chạy mỗi phút, không có khoá thì trader nhận
            // 15 thông báo giống hệt nhau trước mỗi sự kiện.
            SourceKey = $"blackout-position:{tradingAccountId}:{window.FromUtc:O}",
        }, ct);
    }

    private async Task WarnOnClockDriftAsync(
        string symbol, EngineSetting setting, DateTime utcNow, CancellationToken ct)
    {
        // Đồng hồ máy chủ lệch sàn thì MỌI cửa sổ chặn đều lệch theo, và lệch một chiều: hệ thống
        // tưởng còn thời gian trong khi tin đã ra. Không có cách nào phát hiện từ bên trong ngoài
        // việc đối chiếu với dấu thời gian của sàn.
        FundingSnapshot? snapshot;
        try
        {
            snapshot = await _marketData.GetFundingAsync(symbol, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đối chiếu được đồng hồ với sàn cho {Symbol}.", symbol);
            return;
        }

        if (snapshot is null) return;

        var drift = (utcNow - snapshot.RetrievedAtUtc).Duration();
        if (drift.TotalSeconds <= setting.ClockDriftToleranceSeconds) return;

        _logger.LogWarning(
            "Đồng hồ máy chủ lệch sàn {DriftSeconds:0.#}s (ngưỡng {ToleranceSeconds}s). " +
            "serverUtc={ServerUtc:o} exchangeUtc={ExchangeUtc:o}",
            drift.TotalSeconds, setting.ClockDriftToleranceSeconds, utcNow, snapshot.RetrievedAtUtc);

        await _notifications.PublishAsync(new NotificationCreateModel
        {
            Type = NotificationType.SystemHealth,
            Severity = NotificationSeverity.Critical,
            Title = "Đồng hồ máy chủ lệch giờ sàn",
            Message =
                $"Chênh lệch {drift.TotalSeconds:0.#} giây so với sàn (ngưỡng {setting.ClockDriftToleranceSeconds} giây). " +
                "Mọi cửa sổ chặn theo khung giờ đang lệch đúng bằng chừng đó — hãy đồng bộ giờ máy chủ.",
            Source = nameof(PositionManageService),
            SourceKey = $"clock-drift:{utcNow:yyyy-MM-ddTHH}",
        }, ct);
    }
}
