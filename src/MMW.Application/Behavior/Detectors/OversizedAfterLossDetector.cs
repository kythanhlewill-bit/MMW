using System.Globalization;
using System.Text.Json;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Behavior.Detectors;

/// <summary>
/// Tilt/oversize: ngay sau một lệnh thua, kích thước lệnh tăng vọt &gt; TiltSizeIncreasePercent
/// so với trung bình gần đây. Kích thước đo bằng notional = Quantity * EntryPrice.
/// </summary>
public class OversizedAfterLossDetector : IBehaviorDetector
{
    private const int RecentWindow = 10;

    public FlagType Type => FlagType.OversizedAfterLoss;

    public BehaviorSignal? Detect(BehaviorContext ctx)
    {
        var increasePercent = ctx.Settings.TiltSizeIncreasePercent;
        if (increasePercent <= 0m)
            return null;

        var closed = ctx.History.Where(BehaviorContext.IsClosed).ToList();
        if (closed.Count == 0)
            return null;

        // Chỉ xét tilt khi lệnh ngay trước là lệnh thua.
        var previous = closed[^1];
        if (!BehaviorContext.IsLoss(previous))
            return null;

        var recent = closed.TakeLast(RecentWindow).ToList();
        var avgSize = recent.Average(Notional);
        if (avgSize <= 0m)
            return null;

        var currentSize = Notional(ctx.Trade);
        var limit = avgSize * (1m + increasePercent / 100m);
        if (currentSize <= limit)
            return null;

        var actualIncrease = (currentSize - avgSize) / avgSize * 100m;
        var severity = currentSize > avgSize * 2m ? FlagSeverity.Critical : FlagSeverity.Warning;

        var detail = JsonSerializer.Serialize(new
        {
            currentSize,
            averageSize = Math.Round(avgSize, 8),
            increasePercent = Math.Round(actualIncrease, 2),
            thresholdPercent = increasePercent,
        });

        return new BehaviorSignal(
            Type,
            severity,
            $"Kích thước lệnh tăng {actualIncrease.ToString("0.#", CultureInfo.InvariantCulture)}% so với trung bình " +
            $"ngay sau một lệnh thua (ngưỡng {increasePercent.ToString("0.#", CultureInfo.InvariantCulture)}%) — dấu hiệu tilt.",
            detail);
    }

    private static decimal Notional(Trade t) => t.Quantity * t.EntryPrice;
}
