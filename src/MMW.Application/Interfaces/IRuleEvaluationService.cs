using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

public interface IRuleEvaluationService
{
    /// <summary>
    /// Chấm một lệnh theo Rule Engine: tính lại chỉ số rủi ro, sinh & lưu các Flag vi phạm.
    /// Idempotent — chạy lại sẽ thay thế các Flag RuleViolation cũ của lệnh.
    /// </summary>
    Task<IReadOnlyList<Flag>> EvaluateTradeAsync(long tradeId, CancellationToken cancellationToken = default);
}
