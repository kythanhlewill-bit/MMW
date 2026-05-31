using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.Behavior.Detectors;

/// <summary>
/// Revenge trade: vào lệnh mới trong vòng RevengeTradeWindowMinutes sau khi vừa cắt lỗ.
/// Vào càng nhanh sau khi thua → mức độ càng nặng.
/// </summary>
public class RevengeTradeDetector : IBehaviorDetector
{
    public FlagType Type => FlagType.RevengeTrade;

    public BehaviorSignal? Detect(BehaviorContext ctx)
    {
        var window = ctx.Settings.RevengeTradeWindowMinutes;
        if (window <= 0 || ctx.Trade.OpenedAt is null)
            return null;

        // Lệnh thua gần nhất đã đóng trước lệnh hiện tại.
        var lastLoss = ctx.History.LastOrDefault(t => BehaviorContext.IsLoss(t) && t.ClosedAt.HasValue);
        if (lastLoss is null)
            return null;

        var gap = ctx.Trade.OpenedAt.Value - lastLoss.ClosedAt!.Value;
        if (gap < TimeSpan.Zero || gap.TotalMinutes > window)
            return null;

        var minutes = gap.TotalMinutes;
        var severity = minutes <= window / 3.0 ? FlagSeverity.Critical : FlagSeverity.Warning;

        var detail = JsonSerializer.Serialize(new
        {
            minutesAfterLoss = Math.Round(minutes, 1),
            windowMinutes = window,
            previousLossTradeId = lastLoss.Id,
        });

        return new BehaviorSignal(
            Type,
            severity,
            $"Vào lệnh chỉ {Math.Round(minutes)} phút sau khi cắt lỗ (ngưỡng {window} phút) — dấu hiệu revenge trade.",
            detail);
    }
}
