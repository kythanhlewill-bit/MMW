using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine.Rules;

/// <summary>
/// Số lệnh trong ngày không được vượt ngưỡng. Quy ước: ctx.Day.TradeCount là số lệnh
/// ĐÃ vào trước lệnh đang xét → vào thêm khi đã đạt ngưỡng là vi phạm.
/// </summary>
public class MaxTradesPerDayRule : ITradeRule
{
    public FlagType Type => FlagType.MaxTradesPerDayExceeded;

    public RuleViolation? Evaluate(RuleEvaluationContext ctx)
    {
        if (ctx.Day is null || ctx.Settings.MaxTradesPerDay <= 0)
            return null;

        if (ctx.Day.TradeCount < ctx.Settings.MaxTradesPerDay)
            return null;

        var detail = JsonSerializer.Serialize(new
        {
            tradesToday = ctx.Day.TradeCount,
            maxPerDay = ctx.Settings.MaxTradesPerDay,
        });

        return new RuleViolation(
            Type,
            FlagSeverity.Warning,
            $"Đã có {ctx.Day.TradeCount} lệnh trong ngày, đạt/vượt giới hạn {ctx.Settings.MaxTradesPerDay} lệnh/ngày.",
            detail);
    }
}
