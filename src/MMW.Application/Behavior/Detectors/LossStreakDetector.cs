using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.Behavior.Detectors;

/// <summary>
/// Loss streak: đang vào lệnh khi vừa trải qua chuỗi thua liên tiếp ≥ LossStreakThreshold.
/// Chỉ tính các lệnh đã đóng; chuỗi đứt khi gặp 1 lệnh không thua.
/// </summary>
public class LossStreakDetector : IBehaviorDetector
{
    public FlagType Type => FlagType.LossStreak;

    public BehaviorSignal? Detect(BehaviorContext ctx)
    {
        var threshold = ctx.Settings.LossStreakThreshold;
        if (threshold <= 0)
            return null;

        var closed = ctx.History.Where(BehaviorContext.IsClosed).ToList();

        var streak = 0;
        for (var i = closed.Count - 1; i >= 0; i--)
        {
            if (BehaviorContext.IsLoss(closed[i]))
                streak++;
            else
                break;
        }

        if (streak < threshold)
            return null;

        var severity = streak >= threshold * 2 ? FlagSeverity.Critical : FlagSeverity.Warning;

        var detail = JsonSerializer.Serialize(new
        {
            consecutiveLosses = streak,
            threshold,
        });

        return new BehaviorSignal(
            Type,
            severity,
            $"Đang vào lệnh sau {streak} lần thua liên tiếp (ngưỡng {threshold}) — cân nhắc dừng lại.",
            detail);
    }
}
