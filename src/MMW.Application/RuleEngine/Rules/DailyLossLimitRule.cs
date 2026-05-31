using System.Globalization;
using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine.Rules;

/// <summary>Tổng lỗ trong ngày không được vượt % vốn cho phép.</summary>
public class DailyLossLimitRule : ITradeRule
{
    public FlagType Type => FlagType.DailyLossLimitExceeded;

    public RuleViolation? Evaluate(RuleEvaluationContext ctx)
    {
        if (ctx.Day is null || ctx.Settings.MaxDailyLossPercent <= 0m)
            return null;

        // Vốn nền: ưu tiên vốn đầu ngày, fallback vốn hiện tại.
        var equity = ctx.Day.StartingEquity is > 0m ? ctx.Day.StartingEquity!.Value : ctx.AccountEquity;
        if (equity <= 0m)
            return null;

        var lossLimit = equity * ctx.Settings.MaxDailyLossPercent / 100m;

        // NetPnl âm và độ lớn vượt giới hạn lỗ.
        if (ctx.Day.NetPnl >= 0m || -ctx.Day.NetPnl < lossLimit)
            return null;

        var lossPercent = -ctx.Day.NetPnl / equity * 100m;

        var detail = JsonSerializer.Serialize(new
        {
            netPnl = ctx.Day.NetPnl,
            lossPercent = Math.Round(lossPercent, 4),
            maxLossPercent = ctx.Settings.MaxDailyLossPercent,
        });

        return new RuleViolation(
            Type,
            FlagSeverity.Critical,
            $"Lỗ trong ngày {lossPercent.ToString("0.##", CultureInfo.InvariantCulture)}% vượt giới hạn " +
            $"{ctx.Settings.MaxDailyLossPercent.ToString("0.##", CultureInfo.InvariantCulture)}% — nên dừng giao dịch.",
            detail);
    }
}
