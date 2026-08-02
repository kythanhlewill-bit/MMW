namespace MMW.Application.MarketData;

/// <summary>Chỉ số tâm lý thị trường từ nguồn công khai (R-004).</summary>
public interface IMarketSentimentProvider
{
    /// <summary>
    /// Chỉ số sợ hãi/tham lam, 0–100. Trả <c>null</c> khi không truy cập được — khi đó
    /// tiêu chí liên quan nhận 0 điểm theo FR-006 và kế hoạch ngày vẫn sinh được.
    /// </summary>
    Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default);
}
