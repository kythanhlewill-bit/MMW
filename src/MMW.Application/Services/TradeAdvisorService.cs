using Microsoft.Extensions.Logging;
using MMW.Application.Indicators;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Phân tích lệnh đang mở: lấy giá + indicator hiện tại → đưa ra lời khuyên deterministic.
/// Chạy bởi Hangfire job mỗi 3 phút.
/// </summary>
public class TradeAdvisorService : ITradeAdvisorService
{
    private const int CandleLimit = 100;

    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradeAnalysis> _analyses;
    private readonly IMarketDataProvider _marketData;
    private readonly IIndicatorService _indicators;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILlmService _llm;
    private readonly ILogger<TradeAdvisorService> _logger;

    private const string SystemPrompt =
        "Bạn là cố vấn giao dịch crypto chuyên nghiệp. " +
        "Dựa trên dữ liệu indicator và trạng thái lệnh được cung cấp, " +
        "hãy đưa ra lời khuyên ngắn gọn, thực tế bằng tiếng Việt (tối đa 3 câu). " +
        "Tập trung vào hành động cụ thể: giữ lệnh, dời SL, chốt lời một phần, hoặc cắt lỗ. " +
        "Không lặp lại dữ liệu đã cung cấp.";

    public TradeAdvisorService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradeAnalysis> analyses,
        IMarketDataProvider marketData,
        IIndicatorService indicators,
        IUnitOfWork unitOfWork,
        ILlmService llm,
        ILogger<TradeAdvisorService> logger)
    {
        _trades = trades;
        _analyses = analyses;
        _marketData = marketData;
        _indicators = indicators;
        _unitOfWork = unitOfWork;
        _llm = llm;
        _logger = logger;
    }

    public async Task<int> AnalyzeOpenTradesAsync(CancellationToken cancellationToken = default)
    {
        var openTrades = await _trades.FindListAsync(t => t.Status == TradeStatus.Open);
        if (openTrades.Count == 0) return 0;

        var analyzed = 0;
        var symbolGroups = openTrades.GroupBy(t => t.Symbol);

        foreach (var group in symbolGroups)
        {
            try
            {
                var candles = await _marketData.GetCandlesAsync(group.Key, "1h", CandleLimit, cancellationToken);
                if (candles.Count == 0) continue;

                var currentPrice = candles[^1].Close;
                var closes = candles.Select(c => c.Close).ToList();

                decimal? rsi = null, ema20 = null, ema50 = null;
                MarketBias bias = MarketBias.Neutral;

                if (closes.Count >= 50)
                {
                    rsi = _indicators.Rsi(closes, 14);
                    ema20 = _indicators.Ema(closes, 20);
                    ema50 = _indicators.Ema(closes, 50);

                    var biasScore = 0;
                    if (ema20.HasValue && currentPrice > ema20.Value) biasScore++;
                    if (ema20.HasValue && currentPrice < ema20.Value) biasScore--;
                    if (ema50.HasValue && currentPrice > ema50.Value) biasScore++;
                    if (ema50.HasValue && currentPrice < ema50.Value) biasScore--;
                    bias = biasScore > 0 ? MarketBias.Bullish : biasScore < 0 ? MarketBias.Bearish : MarketBias.Neutral;
                }

                foreach (var trade in group)
                {
                    try
                    {
                        var analysis = BuildAnalysis(trade, currentPrice, rsi, ema20, ema50, bias);

                        // Phần tính toán ở trên rẻ và chạy mọi lượt; phần AI thì không. Trước đây
                        // nó gọi vô điều kiện mỗi lượt job, tức 1.440 lời gọi mỗi ngày cho MỘT lệnh
                        // nằm im — lời khuyên y hệt nhau, sinh lại gần nghìn lần.
                        var previous = (await _analyses.FindListAsync(a => a.TradeId == trade.Id)).FirstOrDefault();
                        if (ShouldAskLlm(previous, analysis))
                            await EnhanceWithLlmAsync(trade, analysis, cancellationToken);
                        else if (previous is not null)
                        {
                            // Giữ nguyên lời khuyên AI cũ. Thay bằng lời khuyên tính máy sẽ khiến ô
                            // tư vấn nhấp nháy đổi giọng mỗi vài phút dù chẳng có gì mới.
                            analysis.Advice = previous.Advice;
                            analysis.AiEnhanced = previous.AiEnhanced;
                        }

                        await UpsertAnalysisAsync(trade.Id, analysis);
                        analyzed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze open trade {TradeId} for {Symbol}", trade.Id, trade.Symbol);
                    }
                }

                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze open trades for symbol {Symbol}", group.Key);
            }
        }

        return analyzed;
    }

    private TradeAnalysis BuildAnalysis(Trade trade, decimal currentPrice, decimal? rsi, decimal? ema20, decimal? ema50, MarketBias bias)
    {
        var isLong = trade.Direction == TradeDirection.Long;
        var pnl = isLong
            ? (currentPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - currentPrice) * trade.Quantity;
        var notional = trade.EntryPrice * trade.Quantity;
        var pnlPercent = notional > 0 ? pnl / notional * 100 : 0;

        decimal? distSl = null, distTp = null;
        if (trade.StopLoss.HasValue && trade.StopLoss.Value > 0)
            distSl = (currentPrice - trade.StopLoss.Value) / trade.StopLoss.Value * 100 * (isLong ? 1 : -1);
        if (trade.TakeProfit.HasValue && trade.TakeProfit.Value > 0)
            distTp = (trade.TakeProfit.Value - currentPrice) / currentPrice * 100 * (isLong ? 1 : -1);

        var riskLevel = DetermineRiskLevel(pnlPercent, distSl, rsi, bias, isLong);
        var advice = GenerateAdvice(trade, currentPrice, pnlPercent, distSl, distTp, rsi, ema20, bias, isLong);
        var details = GenerateDetails(trade, currentPrice, pnl, pnlPercent, distSl, distTp, rsi, ema20, ema50, bias);

        return new TradeAnalysis
        {
            TradeId = trade.Id,
            CurrentPrice = currentPrice,
            UnrealizedPnl = Math.Round(pnl, 4),
            UnrealizedPnlPercent = Math.Round(pnlPercent, 4),
            DistanceToSlPercent = distSl.HasValue ? Math.Round(distSl.Value, 4) : null,
            DistanceToTpPercent = distTp.HasValue ? Math.Round(distTp.Value, 4) : null,
            Rsi = rsi,
            Ema20 = ema20,
            Ema50 = ema50,
            Bias = bias,
            RiskLevel = riskLevel,
            Advice = advice,
            Details = details,
            AnalyzedAt = DateTime.UtcNow,
        };
    }

    private static string DetermineRiskLevel(decimal pnlPercent, decimal? distSl, decimal? rsi, MarketBias bias, bool isLong)
    {
        if (distSl.HasValue && distSl.Value < 1m) return "Critical";
        if (pnlPercent < -5m) return "High";
        if (distSl.HasValue && distSl.Value < 3m) return "Warning";
        if (isLong && bias == MarketBias.Bearish) return "Warning";
        if (!isLong && bias == MarketBias.Bullish) return "Warning";
        if (rsi.HasValue && ((isLong && rsi.Value > 75) || (!isLong && rsi.Value < 25))) return "Warning";
        if (pnlPercent > 5m) return "Profit";
        return "Normal";
    }

    private static string GenerateAdvice(Trade trade, decimal price, decimal pnlPct, decimal? distSl, decimal? distTp, decimal? rsi, decimal? ema20, MarketBias bias, bool isLong)
    {
        var parts = new List<string>();

        // Distance warnings
        if (distSl.HasValue && distSl.Value < 1m)
            parts.Add("⚠️ GẦN CHẠM SL! Cân nhắc đóng lệnh để bảo toàn vốn.");
        else if (distSl.HasValue && distSl.Value < 3m)
            parts.Add("Giá đang tiến gần SL. Theo dõi sát.");

        if (distTp.HasValue && distTp.Value < 1m)
            parts.Add("✅ GẦN TP! Có thể chốt một phần lợi nhuận.");

        // Trend conflict
        if (isLong && bias == MarketBias.Bearish)
            parts.Add("Xu hướng ngắn hạn đang giảm — ngược với lệnh Long. Cẩn thận.");
        else if (!isLong && bias == MarketBias.Bullish)
            parts.Add("Xu hướng ngắn hạn đang tăng — ngược với lệnh Short. Cẩn thận.");

        // RSI extremes
        if (rsi.HasValue)
        {
            if (isLong && rsi.Value > 75)
                parts.Add("RSI quá mua (>75) — có thể sắp điều chỉnh. Cân nhắc dời SL lên breakeven.");
            else if (!isLong && rsi.Value < 25)
                parts.Add("RSI quá bán (<25) — có thể sắp hồi. Cân nhắc dời SL về breakeven.");
        }

        // Profit management
        if (pnlPct > 8m)
            parts.Add("Lợi nhuận tốt (>8%). Nên dời SL lên breakeven hoặc chốt một phần.");
        else if (pnlPct > 3m && parts.Count == 0)
            parts.Add("Lệnh đang lãi. Có thể dời SL lên breakeven để bảo vệ lợi nhuận.");

        // Loss management
        if (pnlPct < -5m && (distSl is null || distSl > 3m))
            parts.Add("Lệnh đang lỗ >5% mà chưa gần SL. Xem lại logic vào lệnh.");

        if (parts.Count == 0)
        {
            if (pnlPct >= 0)
                parts.Add("Lệnh đang ổn. Giữ theo kế hoạch, không nên can thiệp sớm.");
            else
                parts.Add("Lệnh chưa lãi nhưng chưa đáng lo. Kiên nhẫn theo kế hoạch.");
        }

        return string.Join(" ", parts);
    }

    private static string GenerateDetails(Trade trade, decimal price, decimal pnl, decimal pnlPct, decimal? distSl, decimal? distTp, decimal? rsi, decimal? ema20, decimal? ema50, MarketBias bias)
    {
        var lines = new List<string>
        {
            $"Giá hiện tại: {price:N4}",
            $"PnL chưa chốt: {pnl:N2} ({pnlPct:N2}%)",
        };

        if (distSl.HasValue) lines.Add($"Khoảng cách SL: {distSl.Value:N2}%");
        if (distTp.HasValue) lines.Add($"Khoảng cách TP: {distTp.Value:N2}%");
        if (rsi.HasValue) lines.Add($"RSI(14): {rsi.Value:N1}");
        if (ema20.HasValue) lines.Add($"EMA20: {ema20.Value:N4}");
        if (ema50.HasValue) lines.Add($"EMA50: {ema50.Value:N4}");
        lines.Add($"Xu hướng: {bias}");

        return string.Join(" | ", lines);
    }

    /// <summary>Khoảng cách tối thiểu giữa hai lần hỏi AI về CÙNG một lệnh.</summary>
    /// <remarks>
    /// Trần cứng, không phải gợi ý: dù giá có nhảy thế nào thì một lệnh cũng chỉ tốn tối đa
    /// 6 lời gọi mỗi giờ. Đây là thứ chặn hoá đơn, hai điều kiện dưới chỉ quyết định có dùng
    /// hết ngạch đó hay không.
    /// </remarks>
    private static readonly TimeSpan LlmMinInterval = TimeSpan.FromMinutes(10);

    /// <summary>Quá hạn này thì làm mới dù giá đứng yên — để lời khuyên không cũ hẳn.</summary>
    private static readonly TimeSpan LlmMaxStale = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Mức đổi lãi/lỗ đủ để hỏi lại sớm, tính theo phần trăm giá trị hợp đồng (đòn bẩy không
    /// nhân vào đây). Nhỏ hơn ngần này thì bối cảnh chưa đổi đủ để AI nói khác đi.
    /// </summary>
    private const decimal LlmPnlDeltaPercent = 0.8m;

    private static bool ShouldAskLlm(TradeAnalysis? previous, TradeAnalysis fresh)
    {
        if (previous is null || !previous.AiEnhanced) return true;

        var age = fresh.AnalyzedAt - previous.AnalyzedAt;
        if (age >= LlmMaxStale) return true;
        if (age < LlmMinInterval) return false;

        return Math.Abs(fresh.UnrealizedPnlPercent - previous.UnrealizedPnlPercent) >= LlmPnlDeltaPercent;
    }

    private async Task EnhanceWithLlmAsync(Trade trade, TradeAnalysis analysis, CancellationToken ct)
    {
        if (!_llm.IsConfigured) return;

        var userMessage =
            $"Lệnh: {trade.Direction} {trade.Symbol}, Entry: {trade.EntryPrice}, Qty: {trade.Quantity}\n" +
            $"SL: {trade.StopLoss?.ToString() ?? "không có"}, TP: {trade.TakeProfit?.ToString() ?? "không có"}\n" +
            $"Giá hiện tại: {analysis.CurrentPrice}, PnL: {analysis.UnrealizedPnlPercent:N2}%\n" +
            $"Khoảng cách SL: {(analysis.DistanceToSlPercent.HasValue ? $"{analysis.DistanceToSlPercent:N2}%" : "N/A")}\n" +
            $"RSI(14): {(analysis.Rsi.HasValue ? $"{analysis.Rsi:N1}" : "N/A")}, " +
            $"EMA20: {(analysis.Ema20.HasValue ? $"{analysis.Ema20:N4}" : "N/A")}, " +
            $"EMA50: {(analysis.Ema50.HasValue ? $"{analysis.Ema50:N4}" : "N/A")}\n" +
            $"Xu hướng: {analysis.Bias}, Mức rủi ro: {analysis.RiskLevel}\n" +
            $"Phân tích cơ bản: {analysis.Advice}";

        var aiAdvice = await _llm.ChatAsync(SystemPrompt, userMessage, ct);
        if (!string.IsNullOrWhiteSpace(aiAdvice))
        {
            analysis.Advice = aiAdvice.Length > 2000 ? aiAdvice[..2000] : aiAdvice;
            analysis.AiEnhanced = true;
        }
    }

    private async Task UpsertAnalysisAsync(long tradeId, TradeAnalysis analysis)
    {
        var existing = (await _analyses.FindListAsync(a => a.TradeId == tradeId)).FirstOrDefault();
        if (existing is not null)
        {
            var tracked = await _analyses.FindAsync(existing.Id);
            if (tracked is not null)
            {
                tracked.CurrentPrice = analysis.CurrentPrice;
                tracked.UnrealizedPnl = analysis.UnrealizedPnl;
                tracked.UnrealizedPnlPercent = analysis.UnrealizedPnlPercent;
                tracked.DistanceToSlPercent = analysis.DistanceToSlPercent;
                tracked.DistanceToTpPercent = analysis.DistanceToTpPercent;
                tracked.Rsi = analysis.Rsi;
                tracked.Ema20 = analysis.Ema20;
                tracked.Ema50 = analysis.Ema50;
                tracked.Bias = analysis.Bias;
                tracked.RiskLevel = analysis.RiskLevel;
                tracked.Advice = analysis.Advice;
                tracked.Details = analysis.Details;
                tracked.AiEnhanced = analysis.AiEnhanced;
                tracked.AnalyzedAt = analysis.AnalyzedAt;
                _analyses.Update(tracked);
                return;
            }
        }

        analysis.TradeId = tradeId;
        await _analyses.AddAsync(analysis);
    }
}
