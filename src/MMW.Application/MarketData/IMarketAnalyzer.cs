using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Phân tích deterministic một chuỗi nến thành indicator + thiên hướng (bias).
/// Không AI — đây là phần "logic cứng". LLM diễn giải để dành cho sau.
/// </summary>
public interface IMarketAnalyzer
{
    MarketAnalysis Analyze(IReadOnlyList<Candle> candles);
}
