using MMW.Domain.Entities;

namespace MMW.Application.RuleEngine;

/// <summary>
/// Dữ liệu đầu vào cho việc chấm rule một lệnh. Các chỉ số (RiskPercent, PlannedRiskReward...)
/// được tính trước bằng <see cref="ITradeMetricsCalculator"/>.
/// </summary>
public sealed class RuleEvaluationContext
{
    public required Trade Trade { get; init; }

    public required RiskSetting Settings { get; init; }

    /// <summary>Vốn tài khoản tại thời điểm chấm (dùng cho daily loss limit).</summary>
    public required decimal AccountEquity { get; init; }

    /// <summary>
    /// Tổng hợp ngày của lệnh (số lệnh đã vào trước lệnh này, NetPnl trong ngày...).
    /// Null nếu chưa có dữ liệu ngày → các rule cấp ngày sẽ bỏ qua.
    /// </summary>
    public TradingDay? Day { get; init; }
}
