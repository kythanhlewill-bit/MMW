using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

public interface IBehaviorAnalysisService
{
    /// <summary>
    /// Phân tích hành vi của một lệnh dựa trên lịch sử giao dịch, sinh & lưu Flag (Category=Behavior).
    /// Idempotent — chạy lại sẽ thay thế các Flag Behavior cũ của lệnh.
    /// </summary>
    Task<IReadOnlyList<Flag>> AnalyzeTradeAsync(long tradeId, CancellationToken cancellationToken = default);
}
