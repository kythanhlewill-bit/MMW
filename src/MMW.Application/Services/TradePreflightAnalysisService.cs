using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MMW.Application.Indicators;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class TradePreflightAnalysisService : ITradePreflightAnalysisService
{
    private const int CandleLimit = 120;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly ISettingsService _settings;
    private readonly IMarketDataProvider _marketData;
    private readonly IIndicatorService _indicators;
    private readonly ILlmService _llm;
    private readonly IMacroEventService _macroEvents;
    private readonly ILiveBalanceService _liveBalance;
    private readonly ILogger<TradePreflightAnalysisService> _logger;

    private const string SystemPrompt =
        "Bạn là bộ lọc trước khi lưu lệnh cho một trading journal crypto futures. " +
        "Nhiệm vụ là đánh giá setup theo kỷ luật rủi ro, không dự đoán chắc chắn thị trường. " +
        "Ưu tiên reject/wait nếu dữ liệu thiếu, RR kém, rủi ro vượt ngưỡng, hoặc setup ngược trend. " +
        "Không khuyến nghị all-in, không bỏ qua stop loss. " +
        "LUÔN đề xuất Stop Loss và Take Profit TỐI ƯU dựa trên ATR/cấu trúc giá và RR tối thiểu của tài khoản — " +
        "kể cả khi reject/wait, vẫn phải đưa suggestedStopLoss/suggestedTakeProfit để người dùng tham khảo. " +
        "Đặt suggestedStopLoss/suggestedTakeProfit là GIÁ tuyệt đối (cùng đơn vị Entry). " +
        "Với LONG: SL < Entry < TP; với SHORT: TP < Entry < SL. " +
        "Chỉ để null khi thiếu dữ liệu giá. " +
        "Chỉ trả một JSON object hợp lệ, không markdown, không code fence, không text ngoài JSON. " +
        "Giữ advice dưới 180 ký tự, mỗi mảng tối đa 2 câu ngắn. " +
        "Schema: {\"decision\":\"accept|reject|wait\",\"score\":0-100,\"confidence\":0-1,\"advice\":\"...\",\"reasons\":[\"...\"],\"riskWarnings\":[\"...\"],\"invalidation\":\"...\",\"suggestedStopLoss\":number|null,\"suggestedTakeProfit\":number|null}.";

    private const string RepairSystemPrompt =
        "Bạn là bộ lọc trade preflight. Chỉ trả một JSON object hợp lệ, không markdown, không giải thích. " +
        "Schema bắt buộc: {\"decision\":\"accept|reject|wait\",\"score\":0-100,\"confidence\":0-1,\"advice\":\"...\",\"reasons\":[\"...\"],\"riskWarnings\":[\"...\"],\"invalidation\":\"...\",\"suggestedStopLoss\":number|null,\"suggestedTakeProfit\":number|null}. " +
        "LUÔN đưa suggestedStopLoss/suggestedTakeProfit (GIÁ tuyệt đối; Long: SL<Entry<TP; Short: TP<Entry<SL) kể cả khi reject. " +
        "Advice dưới 160 ký tự, reasons và riskWarnings tối đa 2 item.";

    public TradePreflightAnalysisService(
        IBaseRepository<TradingAccount> accounts,
        ISettingsService settings,
        IMarketDataProvider marketData,
        IIndicatorService indicators,
        ILlmService llm,
        IMacroEventService macroEvents,
        ILiveBalanceService liveBalance,
        ILogger<TradePreflightAnalysisService> logger)
    {
        _accounts = accounts;
        _settings = settings;
        _marketData = marketData;
        _indicators = indicators;
        _llm = llm;
        _macroEvents = macroEvents;
        _liveBalance = liveBalance;
        _logger = logger;
    }

    public async Task<TradePreflightAnalysisResult> AnalyzeAsync(
        TradePreflightAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        Normalize(request);

        var account = await _accounts.FindAsync(request.TradingAccountId);
        var riskSetting = await _settings.GetRiskSettingAsync(request.TradingAccountId, cancellationToken);
        // Số dư tính rủi ro: ưu tiên số dư Futures USDT THẬT trên Binance, fallback CurrentBalance.
        var balance = account is null ? 0m : await _liveBalance.GetEffectiveBalanceAsync(account, cancellationToken);
        var metrics = await BuildMetricsAsync(request, balance, cancellationToken);
        var macroContext = await _macroEvents.GetContextForTradeAsync(request.Symbol, DateTime.UtcNow, cancellationToken);
        var warnings = BuildDeterministicWarnings(request, account, riskSetting, metrics);
        warnings.AddRange(macroContext.RiskWarnings);
        var deterministic = BuildDeterministicResult(request, metrics, warnings);

        if (!_llm.IsConfigured)
        {
            deterministic.IsAiConfigured = false;
            deterministic.Advice = "AI API chưa được cấu hình. Đây là kiểm tra rule nội bộ: " + deterministic.Advice;
            return deterministic;
        }

        var payload = BuildPromptPayload(request, account, balance, riskSetting, metrics, warnings, macroContext);
        var userMessage = JsonSerializer.Serialize(payload, JsonOptions);
        var raw = await _llm.ChatAsync(SystemPrompt, userMessage, cancellationToken);
        var parsed = ParseAiResult(raw, allowPartial: false);

        if (parsed is null)
        {
            var compactPayload = BuildCompactPromptPayload(request, metrics, warnings, macroContext);
            var compactMessage = JsonSerializer.Serialize(compactPayload, JsonOptions);
            var retryRaw = await _llm.ChatAsync(RepairSystemPrompt, compactMessage, cancellationToken);
            parsed = ParseAiResult(retryRaw, allowPartial: false);
            raw = retryRaw ?? raw;
        }

        parsed ??= ParseAiResult(raw, allowPartial: true);

        if (parsed is null)
        {
            _logger.LogWarning("AI preflight returned invalid JSON for {Symbol}. Raw={Raw}", request.Symbol, TrimRaw(raw));
            deterministic.IsAiConfigured = true;
            deterministic.RiskWarnings.Add("AI không trả về JSON hợp lệ, đang dùng kiểm tra nội bộ.");
            return deterministic;
        }

        parsed.IsAiConfigured = true;
        parsed.AiAnswered = true;   // AI thật đã trả lời hợp lệ
        parsed.Metrics = metrics;
        parsed.Decision = NormalizeDecision(parsed.Decision);
        parsed.Score = Math.Clamp(parsed.Score, 0, 100);
        parsed.Confidence = Math.Clamp(parsed.Confidence, 0m, 1m);
        parsed.Reasons ??= [];
        parsed.RiskWarnings ??= [];
        parsed.Reasons = parsed.Reasons.Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList();
        parsed.RiskWarnings = parsed.RiskWarnings
            .Concat(warnings)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .Take(8)
            .ToList();

        if (string.IsNullOrWhiteSpace(parsed.Advice))
            parsed.Advice = deterministic.Advice;

        ApplyMacroGate(parsed, macroContext, deterministic.Advice);
        SanitizeSuggestedLevels(parsed, request);

        return parsed;
    }

    /// <summary>Bỏ SL/TP do AI đề xuất nếu sai phía Entry hoặc trùng giá trị hiện tại.</summary>
    private static void SanitizeSuggestedLevels(TradePreflightAnalysisResult parsed, TradePreflightAnalysisRequest request)
    {
        var entry = request.EntryPrice;
        if (entry <= 0m)
        {
            parsed.SuggestedStopLoss = null;
            parsed.SuggestedTakeProfit = null;
            return;
        }

        var isLong = request.Direction == TradeDirection.Long;

        // Chỉ bỏ nếu SAI phía Entry (giữ lại kể cả trùng giá hiện tại để LUÔN hiển thị cho user).
        if (parsed.SuggestedStopLoss is decimal sl && !(sl > 0m && (isLong ? sl < entry : sl > entry)))
            parsed.SuggestedStopLoss = null;

        if (parsed.SuggestedTakeProfit is decimal tp && !(tp > 0m && (isLong ? tp > entry : tp < entry)))
            parsed.SuggestedTakeProfit = null;
    }

    private async Task<TradePreflightMetrics> BuildMetricsAsync(
        TradePreflightAnalysisRequest request,
        decimal accountBalance,
        CancellationToken cancellationToken)
    {
        var metrics = new TradePreflightMetrics
        {
            AccountBalance = accountBalance > 0m ? accountBalance : null,
        };

        if (request.EntryPrice > 0m && request.Quantity > 0m && request.StopLoss is > 0m)
        {
            metrics.RiskAmount = Math.Abs(request.EntryPrice - request.StopLoss.Value) * request.Quantity;
            if (accountBalance > 0m)
                metrics.RiskPercent = metrics.RiskAmount / accountBalance * 100m;
        }

        if (request.EntryPrice > 0m && request.StopLoss is > 0m && request.TakeProfit is > 0m)
        {
            var risk = Math.Abs(request.EntryPrice - request.StopLoss.Value);
            var reward = Math.Abs(request.TakeProfit.Value - request.EntryPrice);
            if (risk > 0m)
                metrics.PlannedRiskReward = reward / risk;
        }

        try
        {
            var candles = await _marketData.GetCandlesAsync(request.Symbol, "1h", CandleLimit, cancellationToken);
            if (candles.Count == 0) return metrics;

            var closes = candles.Select(c => c.Close).ToList();
            var macd = _indicators.Macd(closes);

            metrics.CurrentPrice = closes[^1];
            metrics.Rsi14 = _indicators.Rsi(closes);
            metrics.Ema20 = _indicators.Ema(closes, 20);
            metrics.Ema50 = _indicators.Ema(closes, 50);
            metrics.Atr14 = _indicators.Atr(candles);
            metrics.MacdHistogram = macd.Histogram;
            metrics.Bias = DetermineBias(metrics.CurrentPrice, metrics.Ema20, metrics.Ema50, metrics.MacdHistogram);
        }
        catch (Exception ex)
        {
            // Market data lỗi không được chặn user lưu lệnh; AI vẫn có thể review bằng dữ liệu form.
            _logger.LogWarning(ex, "Failed to load market data for preflight analysis {Symbol}", request.Symbol);
        }

        return metrics;
    }

    private static MarketBias DetermineBias(decimal? price, decimal? ema20, decimal? ema50, decimal? macdHistogram)
    {
        var score = 0;
        if (price.HasValue && ema20.HasValue) score += price.Value >= ema20.Value ? 1 : -1;
        if (price.HasValue && ema50.HasValue) score += price.Value >= ema50.Value ? 1 : -1;
        if (macdHistogram.HasValue) score += macdHistogram.Value >= 0m ? 1 : -1;
        return score > 0 ? MarketBias.Bullish : score < 0 ? MarketBias.Bearish : MarketBias.Neutral;
    }

    private static List<string> BuildDeterministicWarnings(
        TradePreflightAnalysisRequest request,
        TradingAccount? account,
        RiskSetting riskSetting,
        TradePreflightMetrics metrics)
    {
        var warnings = new List<string>();

        if (account is null)
            warnings.Add("Không tìm thấy tài khoản giao dịch.");
        if (request.EntryPrice <= 0m)
            warnings.Add("Giá vào phải lớn hơn 0.");
        if (request.Quantity <= 0m)
            warnings.Add("Khối lượng phải lớn hơn 0.");
        if (riskSetting.RequireStopLoss && request.StopLoss is null or <= 0m)
            warnings.Add("Thiếu Stop Loss trong khi cấu hình yêu cầu bắt buộc có SL.");

        if (request.StopLoss is > 0m)
        {
            if (request.Direction == TradeDirection.Long && request.StopLoss >= request.EntryPrice)
                warnings.Add("Long setup có Stop Loss không nằm dưới entry.");
            if (request.Direction == TradeDirection.Short && request.StopLoss <= request.EntryPrice)
                warnings.Add("Short setup có Stop Loss không nằm trên entry.");
        }

        if (request.TakeProfit is > 0m)
        {
            if (request.Direction == TradeDirection.Long && request.TakeProfit <= request.EntryPrice)
                warnings.Add("Long setup có Take Profit không nằm trên entry.");
            if (request.Direction == TradeDirection.Short && request.TakeProfit >= request.EntryPrice)
                warnings.Add("Short setup có Take Profit không nằm dưới entry.");
        }

        if (metrics.RiskPercent.HasValue && metrics.RiskPercent.Value > riskSetting.MaxRiskPerTradePercent)
            warnings.Add($"Rủi ro {metrics.RiskPercent.Value:N2}% vượt ngưỡng {riskSetting.MaxRiskPerTradePercent:N2}% mỗi lệnh.");

        if (metrics.PlannedRiskReward.HasValue && metrics.PlannedRiskReward.Value < riskSetting.MinRiskRewardRatio)
            warnings.Add($"RR dự kiến {metrics.PlannedRiskReward.Value:N2} thấp hơn ngưỡng {riskSetting.MinRiskRewardRatio:N2}.");

        if (request.Direction == TradeDirection.Long && metrics.Bias == MarketBias.Bearish)
            warnings.Add("Lệnh Long đang ngược bias 1h.");
        if (request.Direction == TradeDirection.Short && metrics.Bias == MarketBias.Bullish)
            warnings.Add("Lệnh Short đang ngược bias 1h.");

        return warnings;
    }

    private static TradePreflightAnalysisResult BuildDeterministicResult(
        TradePreflightAnalysisRequest request,
        TradePreflightMetrics metrics,
        List<string> warnings)
    {
        var critical = warnings.Any(w =>
            w.Contains("vượt ngưỡng", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("Thiếu Stop Loss", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("không nằm", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("phải lớn hơn", StringComparison.OrdinalIgnoreCase));

        var decision = critical ? "reject" : warnings.Count > 0 ? "wait" : "accept";
        var score = Math.Clamp(80 - warnings.Count * 15, 0, 100);
        var advice = decision switch
        {
            "accept" => "Setup đạt các kiểm tra cơ bản. Vẫn nên giữ đúng khối lượng và không dời SL xa hơn.",
            "reject" => "Không nên lưu/vào lệnh theo thông số hiện tại. Hãy sửa các lỗi rủi ro trước.",
            _ => "Nên chờ hoặc chỉnh lại setup trước khi lưu lệnh.",
        };

        return new TradePreflightAnalysisResult
        {
            Decision = decision,
            Score = score,
            Confidence = metrics.CurrentPrice.HasValue ? 0.55m : 0.35m,
            Advice = advice,
            Reasons = [BuildSetupSummary(request, metrics)],
            RiskWarnings = warnings,
            Invalidation = request.StopLoss is > 0m
                ? $"Setup mất hiệu lực nếu giá đi qua SL {request.StopLoss.Value:N4}."
                : "Setup mất hiệu lực nếu không xác định được Stop Loss.",
            Metrics = metrics,
        };
    }

    private static object BuildPromptPayload(
        TradePreflightAnalysisRequest request,
        TradingAccount? account,
        decimal balance,
        RiskSetting riskSetting,
        TradePreflightMetrics metrics,
        IReadOnlyList<string> deterministicWarnings,
        MacroEventContext macroContext)
    {
        return new
        {
            instruction = "Đánh giá setup trước khi user lưu vào nhật ký. Không đặt lệnh thật. Nếu bối cảnh macro/news đang ở vùng tin mạnh thì ưu tiên wait.",
            trade = new
            {
                request.Symbol,
                Direction = request.Direction.ToString(),
                OrderType = request.OrderType.ToString(),
                Status = request.Status.ToString(),
                request.EntryPrice,
                request.StopLoss,
                request.TakeProfit,
                request.Quantity,
                request.Leverage,
                request.Fee,
                EmotionBefore = request.EmotionBefore.ToString(),
                request.Note,
            },
            account = account is null
                ? null
                : new
                {
                    account.Name,
                    account.Currency,
                    // Số dư THẬT từ Binance (fallback CurrentBalance) — dùng để AI đánh giá rủi ro.
                    CurrentBalance = balance,
                },
            riskPolicy = new
            {
                riskSetting.RequireStopLoss,
                riskSetting.MaxRiskPerTradePercent,
                riskSetting.MinRiskRewardRatio,
                riskSetting.MaxTradesPerDay,
                riskSetting.MaxDailyLossPercent,
            },
            marketContext1h = new
            {
                metrics.CurrentPrice,
                metrics.Rsi14,
                metrics.Ema20,
                metrics.Ema50,
                metrics.Atr14,
                metrics.MacdHistogram,
                Bias = metrics.Bias.ToString(),
            },
            computedRisk = new
            {
                metrics.RiskAmount,
                metrics.RiskPercent,
                metrics.PlannedRiskReward,
            },
            deterministicWarnings,
            macroContext = new
            {
                macroContext.IsConfigured,
                macroContext.HasBlockingEvent,
                macroContext.Summary,
                events = macroContext.Events.Take(5).Select(e => new
                {
                    e.Kind,
                    e.Impact,
                    e.Title,
                    e.Currency,
                    e.OccursAtUtc,
                    e.Source,
                }),
            },
            outputRules = new
            {
                decision = "accept chỉ khi setup hợp lệ, rủi ro hợp lý và không ngược bối cảnh quá rõ; reject khi rủi ro sai; wait khi thiếu dữ liệu hoặc cần chờ xác nhận.",
                score = "0-100, dưới 50 thường reject/wait.",
                confidence = "0-1, không vượt 0.75 vì đây là phân tích trước lệnh, không phải dự đoán chắc chắn.",
            },
        };
    }

    private static object BuildCompactPromptPayload(
        TradePreflightAnalysisRequest request,
        TradePreflightMetrics metrics,
        IReadOnlyList<string> deterministicWarnings,
        MacroEventContext macroContext)
    {
        return new
        {
            trade = new
            {
                request.Symbol,
                Direction = request.Direction.ToString(),
                request.EntryPrice,
                request.StopLoss,
                request.TakeProfit,
                request.Quantity,
            },
            risk = new
            {
                metrics.RiskPercent,
                metrics.PlannedRiskReward,
            },
            market = new
            {
                metrics.CurrentPrice,
                metrics.Rsi14,
                metrics.Ema20,
                metrics.Ema50,
                Bias = metrics.Bias.ToString(),
            },
            warnings = deterministicWarnings.Take(5),
            macro = new
            {
                macroContext.HasBlockingEvent,
                macroContext.Summary,
            },
        };
    }

    private static TradePreflightAnalysisResult? ParseAiResult(string? raw, bool allowPartial)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        foreach (var json in ExtractJsonCandidates(raw))
        {
            var parsed = TryReadCanonicalAiResult(json);
            if (parsed is not null) return parsed;

            parsed = TryDeserializeAiResult(json);
            if (parsed is not null) return parsed;

            if (allowPartial)
            {
                parsed = TryParsePartialAiResult(json);
                if (parsed is not null) return parsed;
            }
        }

        return null;
    }

    private static TradePreflightAnalysisResult? TryReadCanonicalAiResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = UnwrapAiRoot(doc.RootElement);

            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                return string.IsNullOrWhiteSpace(inner) ? null : ParseAiResult(inner, allowPartial: false);
            }

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var decision = ReadString(root, "decision", "Decision", "action", "verdict");
            var advice = ReadString(root, "advice", "Advice", "summary", "message", "recommendation");
            var score = ReadInt(root, "score", "Score", "rating");
            var confidence = ReadDecimal(root, "confidence", "Confidence");
            var reasons = ReadStringList(root, "reasons", "Reasons", "reason");
            var warnings = ReadStringList(root, "riskWarnings", "RiskWarnings", "warnings", "risks");
            var invalidation = ReadString(root, "invalidation", "Invalidation", "invalidatedBy", "stopCondition");
            var suggestedSl = ReadDecimal(root, "suggestedStopLoss", "SuggestedStopLoss", "adjustedStopLoss", "newStopLoss");
            var suggestedTp = ReadDecimal(root, "suggestedTakeProfit", "SuggestedTakeProfit", "adjustedTakeProfit", "newTakeProfit");

            if (decision is null && advice is null && score is null && confidence is null && reasons.Count == 0 && warnings.Count == 0)
                return null;

            return new TradePreflightAnalysisResult
            {
                Decision = decision ?? "wait",
                Score = score ?? 0,
                Confidence = NormalizeConfidence(confidence),
                Advice = advice ?? "",
                Reasons = reasons,
                RiskWarnings = warnings,
                Invalidation = invalidation ?? "",
                SuggestedStopLoss = suggestedSl,
                SuggestedTakeProfit = suggestedTp,
            };
        }
        catch
        {
            return null;
        }
    }

    private static TradePreflightAnalysisResult? TryDeserializeAiResult(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TradePreflightAnalysisResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static TradePreflightAnalysisResult? TryParsePartialAiResult(string json)
    {
        var decision = GetStringField(json, "decision");
        var advice = GetStringField(json, "advice");
        var score = GetIntField(json, "score");
        var confidence = GetDecimalField(json, "confidence");
        var reasons = GetStringArrayField(json, "reasons");
        var warnings = GetStringArrayField(json, "riskWarnings");
        var invalidation = GetStringField(json, "invalidation");
        var suggestedSl = GetDecimalField(json, "suggestedStopLoss");
        var suggestedTp = GetDecimalField(json, "suggestedTakeProfit");

        if (decision is null && advice is null && score is null && confidence is null)
            return null;

        return new TradePreflightAnalysisResult
        {
            Decision = decision ?? "wait",
            Score = score ?? 0,
            Confidence = confidence ?? 0m,
            Advice = advice ?? "",
            Reasons = reasons,
            RiskWarnings = warnings,
            Invalidation = invalidation ?? "",
            SuggestedStopLoss = suggestedSl,
            SuggestedTakeProfit = suggestedTp,
        };
    }

    private static string? GetStringField(string json, string field)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success) return null;
        return UnescapeJsonString(match.Groups["value"].Value);
    }

    private static int? GetIntField(string json, string field)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*(?<value>-?\\d+)",
            RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups["value"].Value, out var value)
            ? value
            : null;
    }

    private static decimal? GetDecimalField(string json, string field)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*(?<value>-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);

        return match.Success && decimal.TryParse(match.Groups["value"].Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static List<string> GetStringArrayField(string json, string field)
    {
        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*\\[(?<value>.*?)\\]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success) return [];

        return Regex.Matches(match.Groups["value"].Value, "\"(?<value>(?:\\\\.|[^\"\\\\])*)\"")
            .Select(m => UnescapeJsonString(m.Groups["value"].Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(5)
            .ToList();
    }

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch
        {
            return value.Replace("\\\"", "\"").Replace("\\n", " ").Replace("\\r", " ");
        }
    }

    private static string ExtractJson(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd >= 0)
                text = text[(firstLineEnd + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static IEnumerable<string> ExtractJsonCandidates(string raw)
    {
        var text = raw.Trim();
        yield return text;

        var unfenced = ExtractJson(text);
        if (!string.Equals(unfenced, text, StringComparison.Ordinal))
            yield return unfenced;

        foreach (var candidate in ExtractBalancedObjects(unfenced))
            yield return candidate;
    }

    private static IEnumerable<string> ExtractBalancedObjects(string text)
    {
        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (ch == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0)
                    yield return text[start..(i + 1)];
            }
        }
    }

    private static JsonElement UnwrapAiRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return root;

        foreach (var name in new[] { "analysis", "result", "data", "response", "preflight" })
        {
            if (TryGetProperty(root, name, out var nested) && nested.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                return nested;
        }

        return root;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return null;
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        var raw = ReadString(root, names);
        if (int.TryParse(raw, out var value))
            return value;

        foreach (var name in names)
        {
            if (TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
                return value;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, params string[] names)
    {
        var raw = ReadString(root, names)?.TrimEnd('%');
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        foreach (var name in names)
        {
            if (TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out parsed))
                return parsed;
        }

        return null;
    }

    private static List<string> ReadStringList(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Take(5)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return [];

                return text.Split(['\n', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(5)
                    .ToList();
            }
        }

        return [];
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static decimal NormalizeConfidence(decimal? confidence)
    {
        if (confidence is null)
            return 0m;

        return confidence.Value > 1m ? confidence.Value / 100m : confidence.Value;
    }

    private static void ApplyMacroGate(
        TradePreflightAnalysisResult parsed,
        MacroEventContext macroContext,
        string deterministicAdvice)
    {
        if (!macroContext.HasBlockingEvent || parsed.Decision != "accept")
            return;

        parsed.Decision = "wait";

        var warning = macroContext.RiskWarnings.FirstOrDefault()
            ?? "Đang gần khung giờ tin mạnh, nên chờ thêm trước khi vào lệnh.";
        parsed.RiskWarnings.Insert(0, warning);

        if (string.IsNullOrWhiteSpace(parsed.Advice) || parsed.Advice == deterministicAdvice)
            parsed.Advice = "Setup kỹ thuật có thể ổn, nhưng đang gần vùng tin mạnh. Chờ tin ra và spread/volatility ổn định rồi đánh giá lại.";
    }

    private static string NormalizeDecision(string? decision)
    {
        return decision?.Trim().ToLowerInvariant() switch
        {
            "accept" => "accept",
            "reject" => "reject",
            _ => "wait",
        };
    }

    private static string BuildSetupSummary(TradePreflightAnalysisRequest request, TradePreflightMetrics metrics)
    {
        var rr = metrics.PlannedRiskReward.HasValue ? metrics.PlannedRiskReward.Value.ToString("N2") : "N/A";
        var risk = metrics.RiskPercent.HasValue ? $"{metrics.RiskPercent.Value:N2}%" : "N/A";
        return $"{request.Direction} {request.Symbol} entry {request.EntryPrice:N4}, RR {rr}, risk {risk}, bias {metrics.Bias}.";
    }

    private static void Normalize(TradePreflightAnalysisRequest request)
    {
        request.Symbol = request.Symbol.Trim().ToUpperInvariant();
        request.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    }

    private static string? TrimRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Length > 1000 ? raw[..1000] : raw;
    }
}
