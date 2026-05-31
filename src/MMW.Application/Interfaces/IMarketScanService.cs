namespace MMW.Application.Interfaces;

public sealed record ScanResult(int Scanned, int Failed);

public interface IMarketScanService
{
    /// <summary>
    /// Quét toàn bộ watchlist đang bật: lấy nến, tính indicator, phân tích, upsert MarketSnapshot.
    /// Lỗi ở một symbol không làm hỏng cả lượt quét.
    /// </summary>
    Task<ScanResult> ScanAllAsync(CancellationToken cancellationToken = default);
}
