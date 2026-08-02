using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Phân tích deterministic một chuỗi nến thành indicator + thiên hướng (bias).
/// Không AI — đây là phần "logic cứng". LLM diễn giải để dành cho sau.
/// </summary>
public interface IMarketAnalyzer
{
    /// <summary>
    /// Tính chỉ báo trên các nến ĐÃ ĐÓNG của <paramref name="candles"/>; nến đang chạy ở cuối
    /// chuỗi bị bỏ qua (FR-001).
    /// </summary>
    /// <param name="currentPrice">
    /// Giá hiện tại, truyền vào từ bên ngoài. KHÔNG lấy từ nến cuối chuỗi: nến đó có thể
    /// đang chạy, và dùng nó cho phép tính chỉ báo chính là lỗi repaint.
    /// </param>
    MarketAnalysis Analyze(IReadOnlyList<Candle> candles, decimal currentPrice);
}
