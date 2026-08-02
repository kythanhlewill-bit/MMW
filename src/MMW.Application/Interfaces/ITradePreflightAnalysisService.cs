using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface ITradePreflightAnalysisService
{
    /// <summary>
    /// Phân tích lệnh user chuẩn bị lưu, kết hợp chỉ số deterministic và LLM nếu đã cấu hình.
    /// Không ghi DB và không đặt lệnh lên sàn.
    /// </summary>
    Task<TradePreflightAnalysisResult> AnalyzeAsync(
        TradePreflightAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
