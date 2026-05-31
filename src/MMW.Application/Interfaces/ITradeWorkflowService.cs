using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

/// <summary>Kết quả phân tích một lệnh: cờ vi phạm rule + cờ hành vi.</summary>
public sealed record TradeAnalysisResult(
    IReadOnlyList<Flag> RuleFlags,
    IReadOnlyList<Flag> BehaviorFlags)
{
    public int TotalFlags => RuleFlags.Count + BehaviorFlags.Count;
}

public interface ITradeWorkflowService
{
    /// <summary>
    /// Chạy toàn bộ phân tích cho một lệnh vừa lưu: chấm Rule Engine, phân tích Behavior,
    /// rồi cập nhật tổng hợp ngày (TradingDay) cho lệnh kế tiếp + dashboard.
    /// </summary>
    Task<TradeAnalysisResult> ProcessTradeAsync(long tradeId, CancellationToken cancellationToken = default);
}
