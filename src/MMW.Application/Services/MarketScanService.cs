using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.Helpers;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Thân Hangfire job: quét watchlist → lấy nến → phân tích →
/// (1) append lịch sử chỉ số, (2) upsert snapshot mới nhất, (3) sinh & lưu đề xuất lệnh.
/// </summary>
public class MarketScanService : IMarketScanService
{
    /// <summary>Số nến lấy mỗi lần (đủ cho EMA50/MACD).</summary>
    private const int CandleLimit = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string AiSignalSystemPrompt = """
Bạn là AI analyst chuyên USDT-M Futures.

Mục tiêu:
- Chỉ đưa ra DUY NHẤT 1 quyết định: LONG, SHORT hoặc WAIT.
- Ưu tiên bảo toàn vốn hơn tìm kiếm lợi nhuận.
- Không dự đoán chắc chắn.
- Không giao dịch khi tín hiệu thiếu đồng thuận hoặc có rủi ro sự kiện vĩ mô lớn.

QUY TẮC PHÂN TÍCH:

1. MARKET STRUCTURE
- Xác định xu hướng chính trên khung lớn nhất được cung cấp.
- Chỉ LONG khi cấu trúc Higher High + Higher Low hoặc breakout được xác nhận.
- Chỉ SHORT khi cấu trúc Lower High + Lower Low hoặc breakdown được xác nhận.
- Nếu sideway hoặc cấu trúc không rõ => WAIT.

2. TREND FILTER
LONG ưu tiên khi: Price > EMA50, EMA20 > EMA50, EMA50 >= EMA200.
SHORT ưu tiên khi: Price < EMA50, EMA20 < EMA50, EMA50 <= EMA200.
Nếu EMA mâu thuẫn mạnh => WAIT.

3. MOMENTUM FILTER
LONG: RSI 50-70, MACD bullish hoặc histogram tăng.
SHORT: RSI 30-50, MACD bearish hoặc histogram giảm.
RSI quá mua/quá bán cực đoan cần thận trọng.

4. VOLATILITY FILTER
- Dùng ATR để tính khoảng stop hợp lý.
- Không đặt stop quá gần (<0.8 ATR) hoặc quá xa (>3 ATR).
- Ưu tiên stop nằm ngoài swing high/swing low gần nhất.

5. ENTRY LOGIC
LONG: Entry tại vùng pullback EMA20/EMA50 hoặc breakout retest thành công.
SHORT: Entry tại vùng pullback EMA20/EMA50 hoặc breakdown retest thất bại.
Không entry đuổi giá khi đã chạy >1.5 ATR khỏi vùng xác nhận.

6. STOP LOSS
LONG: Dưới swing low gần nhất hoặc vùng hỗ trợ quan trọng, tối thiểu 1 ATR khỏi entry.
SHORT: Trên swing high gần nhất hoặc vùng kháng cự quan trọng, tối thiểu 1 ATR khỏi entry.

7. TAKE PROFIT
- TP dựa trên vùng hỗ trợ/kháng cự tiếp theo.
- RR tối thiểu 1:1. Xu hướng mạnh có thể dùng RR 1:3.
- Nếu không tìm được TP đạt RR >= 1:1 => WAIT.

8. NEWS FILTER
Trả WAIT nếu: CPI/FOMC/NFP sắp công bố, tin regulation lớn, ETF news lớn, market shock bất thường, hoặc dữ liệu tin tức thiếu/không chắc chắn.

9. RISK CONTROL
- Không đề xuất nếu stop > 3% giá tài sản.
- Không đề xuất nếu RR < 1:2.
- Không đề xuất nếu confidence < 0.55.

SCORING: 0=Không giao dịch, 1=Rất yếu, 2=Yếu, 3=Khá, 4=Mạnh, 5=Rất mạnh.
CONFIDENCE: 0.00-0.80. Không bao giờ vượt 0.80.

OUTPUT — Chỉ trả về JSON hợp lệ, không markdown, không giải thích:
{"action":"long|short|wait","score":0-5,"confidence":0-1,"entry":number|null,"stopLoss":number|null,"takeProfit":number|null,"riskReward":number|null,"reason":"<220 ký tự","invalidation":"điều kiện làm setup mất hiệu lực","warnings":["...","..."]}

Ràng buộc: LONG: stopLoss < entry < takeProfit. SHORT: takeProfit < entry < stopLoss. WAIT: entry, stopLoss, takeProfit, riskReward = null.
""";

    private const string AiSignalRepairPrompt =
        "Chỉ trả một JSON object hợp lệ theo schema: {\"action\":\"long|short|wait\",\"score\":0-5,\"confidence\":0-1,\"entry\":number|null,\"stopLoss\":number|null,\"takeProfit\":number|null,\"riskReward\":number|null,\"reason\":\"...\",\"invalidation\":\"...\",\"warnings\":[\"...\"]}. " +
        "Không markdown, không giải thích. LONG cần SL<Entry<TP; SHORT cần TP<Entry<SL; nếu không đủ dữ liệu thì action=wait.";

    private readonly IBaseRepository<WatchItem> _watchItems;
    private readonly IBaseRepository<MarketSnapshot> _snapshots;
    private readonly IBaseRepository<IndicatorRecord> _history;
    private readonly IBaseRepository<TradeSignal> _signals;
    private readonly IBaseRepository<AiSignalScanRecord> _aiSignalAudits;
    private readonly IBaseRepository<EntryScorecard> _scorecards;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IMarketDataProvider _marketData;
    private readonly IMarketAnalyzer _analyzer;
    private readonly ITradePreflightAnalysisService _preflightAnalysis;
    private readonly ISettingsService _settings;
    private readonly ILlmService _llm;
    private readonly IMacroEventService _macroEvents;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MarketScanService> _logger;

    public MarketScanService(
        IBaseRepository<WatchItem> watchItems,
        IBaseRepository<MarketSnapshot> snapshots,
        IBaseRepository<IndicatorRecord> history,
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<AiSignalScanRecord> aiSignalAudits,
        IBaseRepository<EntryScorecard> scorecards,
        IBaseRepository<TradingAccount> accounts,
        IMarketDataProvider marketData,
        IMarketAnalyzer analyzer,
        ITradePreflightAnalysisService preflightAnalysis,
        ISettingsService settings,
        ILlmService llm,
        IMacroEventService macroEvents,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        ILogger<MarketScanService> logger)
    {
        _watchItems = watchItems;
        _snapshots = snapshots;
        _history = history;
        _signals = signals;
        _aiSignalAudits = aiSignalAudits;
        _scorecards = scorecards;
        _accounts = accounts;
        _marketData = marketData;
        _analyzer = analyzer;
        _preflightAnalysis = preflightAnalysis;
        _settings = settings;
        _llm = llm;
        _macroEvents = macroEvents;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _watchItems.FindListAsync(w => w.IsActive);
        var appSetting = await _settings.GetAppSettingAsync(cancellationToken);
        var minScore = appSetting.MinSignalScore;
        var scanned = 0;
        var failed = 0;

        foreach (var item in items)
        {
            try
            {
                var candles = await _marketData.GetCandlesAsync(item.Symbol, item.Interval, CandleLimit, cancellationToken);
                if (candles.Count == 0)
                {
                    failed++;
                    continue;
                }

                var now = DateTime.UtcNow;
                // Nến cuối chuỗi là cây đang chạy: giá đóng của nó CHÍNH LÀ giá khớp gần nhất,
                // nên dùng làm giá hiện tại là đúng. Cái không được phép là để nó lọt vào
                // phép tính chỉ báo — việc đó do ClosedOnly() bên trong Analyze chặn.
                var a = _analyzer.Analyze(candles, candles[^1].Close);

                await _history.AddAsync(BuildHistory(item, a, now));
                await UpsertSnapshotAsync(item, a, now);

                // Toàn bộ đường sinh tín hiệu bằng AI nằm sau công tắc shadow. Khi tắt,
                // không gọi macro/preflight/LLM và không tạo audit AI nào (FR-059).
                if (!appSetting.ShadowComparisonEnabled)
                {
                    await _unitOfWork.CommitAsync(cancellationToken);
                    scanned++;
                    continue;
                }

                TradeSignal? signalEntity = null;
                var macroContext = await _macroEvents.GetContextForTradeAsync(item.Symbol, now, cancellationToken);
                var (signal, audit) = await GenerateAiSignalAsync(
                    item, candles, a, appSetting.DefaultTradingAccountId, macroContext, minScore, cancellationToken);
                if (signal is not null)
                {
                    signalEntity = BuildSignal(item, signal, now);
                    var review = await ReviewSignalAsync(signalEntity, appSetting.DefaultTradingAccountId, cancellationToken);

                    // Preflight có thể đề xuất lại SL/TP hợp lý hơn → cập nhật vào đề xuất + tính lại RR.
                    ApplyAiLevels(signalEntity, review);

                    // Lưu đề xuất đến từ AI Signal, kèm kết quả preflight để user thấy bối cảnh.
                    signalEntity.Reason = BuildReviewedReason(signalEntity.Reason ?? "", review);
                    await _signals.AddAsync(signalEntity);

                    // (A) Chỉ auto-tạo lệnh khi AI THẬT trả lời (không phải fallback rule nội bộ) và accept.
                    var aiAccepted = review.AiAnswered
                        && string.Equals(review.Decision, "accept", StringComparison.OrdinalIgnoreCase);
                    if (!aiAccepted)
                        _logger.LogInformation(
                            "Đề xuất {Symbol} {Direction} KHÔNG auto-tạo. AiAnswered={AiAnswered}, Decision={Decision}, Score={Score}.",
                            signalEntity.Symbol, signalEntity.Direction, review.AiAnswered, review.Decision, review.Score);
                }

                await RecordDisagreementAsync(audit, now, cancellationToken);

                await _unitOfWork.CommitAsync(cancellationToken);

                if (signalEntity is not null)
                {
                    await NotifySignalCreatedAsync(signalEntity, cancellationToken);
                }

                scanned++;
            }
            catch (Exception ex)
            {
                // Một symbol lỗi (mạng/symbol sai) không chặn các symbol khác.
                _logger.LogWarning(ex, "Market scan failed for {Symbol} {Interval}", item.Symbol, item.Interval);
                failed++;
            }
        }

        return new ScanResult(scanned, failed);
    }

    private async Task<(SuggestedSignal? Signal, AiSignalScanRecord Audit)> GenerateAiSignalAsync(
        WatchItem item,
        IReadOnlyList<Candle> candles,
        MarketAnalysis analysis,
        long? defaultAccountId,
        MacroEventContext macroContext,
        int minScore,
        CancellationToken cancellationToken)
    {
        var audit = BuildAiSignalAudit(item, analysis, DateTime.UtcNow);
        await _aiSignalAudits.AddAsync(audit);

        if (!_llm.IsConfigured)
        {
            audit.Status = "NotConfigured";
            audit.RejectReason = "AI API chưa cấu hình.";
            _logger.LogInformation("Không sinh đề xuất cho {Symbol}: AI API chưa cấu hình.", item.Symbol);
            return (null, audit);
        }

        var account = defaultAccountId is long accountId
            ? await _accounts.FindAsync(accountId)
            : (await _accounts.FindListAsync(a => a.IsActive)).OrderBy(a => a.Id).FirstOrDefault();
        var risk = account is null
            ? new RiskSetting()
            : await _settings.GetRiskSettingAsync(account.Id, cancellationToken);

        var payload = BuildAiSignalPayload(item, candles, analysis, account, risk, macroContext, minScore);
        audit.Status = "Configured";
        audit.SystemPrompt = AiSignalSystemPrompt;
        audit.RequestJson = JsonSerializer.Serialize(payload, JsonOptions);

        var raw = await _llm.ChatAsync(AiSignalSystemPrompt, audit.RequestJson, cancellationToken);
        audit.ResponseJson = raw;
        var parsed = ParseAiSignal(raw);

        if (parsed is null)
        {
            var compactPayload = BuildCompactAiSignalPayload(item, analysis, macroContext, minScore);
            var retryRaw = await _llm.ChatAsync(AiSignalRepairPrompt, JsonSerializer.Serialize(compactPayload, JsonOptions), cancellationToken);
            audit.RepairResponseJson = retryRaw;
            parsed = ParseAiSignal(retryRaw);
            raw = retryRaw ?? raw;
        }

        if (parsed is null)
        {
            audit.Status = "InvalidJson";
            audit.RejectReason = "AI không trả JSON hợp lệ.";
            _logger.LogWarning("AI signal returned invalid JSON for {Symbol}. Raw={Raw}", item.Symbol, TrimRaw(raw));
            return (null, audit);
        }

        var action = NormalizeAction(parsed.Action);
        parsed.Score = Math.Clamp(parsed.Score, 0, 5);
        parsed.Confidence = Math.Clamp(parsed.Confidence, 0m, 1m);
        ApplyParsedAudit(audit, action, parsed);

        if (action == "wait")
        {
            audit.Status = "Wait";
            audit.RejectReason = "AI chọn WAIT.";
            _logger.LogInformation("AI đề xuất WAIT cho {Symbol}: {Reason}", item.Symbol, parsed.Reason);
            return (null, audit);
        }

        if (parsed.Score < minScore)
        {
            audit.Status = "Rejected";
            audit.RejectReason = $"Score {parsed.Score} thấp hơn min {minScore}.";
            _logger.LogInformation("AI signal {Symbol} bị bỏ qua vì score {Score} < min {MinScore}.", item.Symbol, parsed.Score, minScore);
            return (null, audit);
        }

        if (!TryBuildSuggestedSignal(action, parsed, analysis, risk, out var signal, out var rejectReason))
        {
            audit.Status = "Rejected";
            audit.RejectReason = rejectReason;
            _logger.LogInformation("AI signal {Symbol} bị bỏ qua: {Reason}", item.Symbol, rejectReason);
            return (null, audit);
        }

        audit.Status = "Accepted";
        audit.RiskReward = signal.RiskReward;
        return (signal, audit);
    }

    private async Task RecordDisagreementAsync(
        AiSignalScanRecord audit, DateTime scannedAt, CancellationToken cancellationToken)
    {
        var from = scannedAt.AddHours(-1);
        var deterministic = await _scorecards.Queryable.AsNoTracking()
            .Where(x => !x.IsBacktest && x.Symbol == audit.Symbol
                && x.EvaluatedAtUtc >= from && x.EvaluatedAtUtc <= scannedAt.AddMinutes(2))
            .OrderByDescending(x => x.EvaluatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (deterministic is null)
        {
            audit.IsDisagreement = null;
            audit.DisagreementReason = "Chưa có phiếu tất định cùng symbol trong cửa sổ 1 giờ.";
            return;
        }

        audit.EntryScorecardId = deterministic.Id;
        audit.DeterministicOutcome = deterministic.Outcome.ToString();
        audit.DeterministicDirection = deterministic.Direction?.ToString();
        audit.DeterministicScore = deterministic.TotalScore;

        var aiProposes = audit.Status == "Accepted" && audit.Action is "long" or "short";
        var deterministicProposes = deterministic.Outcome == ScorecardOutcome.Entered;
        var sameDirection = !aiProposes || !deterministicProposes
            || string.Equals(audit.Action, deterministic.Direction?.ToString(), StringComparison.OrdinalIgnoreCase);

        audit.IsDisagreement = aiProposes != deterministicProposes || !sameDirection;
        audit.DisagreementReason = audit.IsDisagreement == true
            ? $"AI={audit.Action ?? "wait"}/{audit.Status}; tất định={deterministic.Direction?.ToString() ?? "không hướng"}/{deterministic.Outcome}, điểm {deterministic.TotalScore}."
            : $"Hai đường đồng thuận: {(aiProposes ? audit.Action : "không vào lệnh")}.";
    }

    private static AiSignalScanRecord BuildAiSignalAudit(WatchItem item, MarketAnalysis analysis, DateTime scannedAt) => new()
    {
        Symbol = item.Symbol,
        Interval = item.Interval,
        ScannedAt = scannedAt,
        Price = analysis.Price,
        Rsi = analysis.Rsi,
        Ema20 = analysis.Ema20,
        Ema50 = analysis.Ema50,
        MacdHistogram = analysis.MacdHistogram,
        Atr = analysis.Atr,
        Status = "Created",
    };

    private static void ApplyParsedAudit(AiSignalScanRecord audit, string action, AiSignalResponse parsed)
    {
        audit.Status = "Responded";
        audit.Action = action;
        audit.Score = parsed.Score;
        audit.Confidence = parsed.Confidence;
        audit.Entry = parsed.Entry;
        audit.StopLoss = parsed.StopLoss;
        audit.TakeProfit = parsed.TakeProfit;
        audit.AiReason = string.IsNullOrWhiteSpace(parsed.Reason)
            ? null
            : parsed.Reason.Length > 500 ? parsed.Reason[..500] : parsed.Reason;
    }

    private static object BuildAiSignalPayload(
        WatchItem item,
        IReadOnlyList<Candle> candles,
        MarketAnalysis analysis,
        TradingAccount? account,
        RiskSetting risk,
        MacroEventContext macroContext,
        int minScore)
    {
        return new
        {
            task = "Hãy phân tích và trả về một trade signal từ AI. Nếu không đủ edge thì WAIT, không ép có lệnh.",
            currentUtc = DateTime.UtcNow,
            symbol = new
            {
                item.Symbol,
                item.Interval,
                Quote = item.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? "USDT" : null,
            },
            riskPolicy = new
            {
                minScore,
                risk.RequireStopLoss,
                risk.MaxRiskPerTradePercent,
                risk.MinRiskRewardRatio,
                risk.MaxTradesPerDay,
                risk.MaxDailyLossPercent,
                defaultLeverage = 20,
                minOrderNotionalUsdt = 20,
            },
            account = account is null
                ? null
                : new
                {
                    account.Name,
                    account.Currency,
                    account.CurrentBalance,
                },
            technicalSnapshot = new
            {
                analysis.Price,
                analysis.Rsi,
                analysis.Ema20,
                analysis.Ema50,
                analysis.Macd,
                analysis.MacdSignal,
                analysis.MacdHistogram,
                analysis.Atr,
                Bias = analysis.Bias.ToString(),
                deterministicScore = analysis.Score,
                analysis.Notes,
            },
            recentCandles = candles
                .TakeLast(24)
                .Select(c => new
                {
                    c.OpenTime,
                    c.Open,
                    c.High,
                    c.Low,
                    c.Close,
                    c.Volume,
                }),
            marketNews = new
            {
                macroContext.IsConfigured,
                macroContext.HasBlockingEvent,
                macroContext.Summary,
                warnings = macroContext.RiskWarnings.Take(3),
                events = macroContext.Events.Take(6).Select(e => new
                {
                    e.Kind,
                    e.Impact,
                    e.Title,
                    e.Summary,
                    e.Currency,
                    e.OccursAtUtc,
                    e.Source,
                }),
            },
            outputRules = new
            {
                action = "long/short chỉ khi edge rõ; wait khi tin mạnh, indicator mâu thuẫn, ATR quá xấu hoặc không thấy entry hợp lý.",
                entry = "Giá tuyệt đối. Có thể là current price hoặc vùng limit hợp lý gần cấu trúc hiện tại.",
                stopLoss = "Giá tuyệt đối, đặt theo swing/ATR, không đặt quá sát nhiễu.",
                takeProfit = "Giá tuyệt đối, ưu tiên đạt RR tối thiểu và vùng thanh khoản/kháng cự hỗ trợ.",
            },
        };
    }

    private static object BuildCompactAiSignalPayload(
        WatchItem item,
        MarketAnalysis analysis,
        MacroEventContext macroContext,
        int minScore)
    {
        return new
        {
            symbol = item.Symbol,
            interval = item.Interval,
            minScore,
            market = new
            {
                analysis.Price,
                analysis.Rsi,
                analysis.Ema20,
                analysis.Ema50,
                analysis.MacdHistogram,
                analysis.Atr,
                Bias = analysis.Bias.ToString(),
                analysis.Notes,
            },
            news = new
            {
                macroContext.HasBlockingEvent,
                macroContext.Summary,
                warnings = macroContext.RiskWarnings.Take(3),
            },
        };
    }

    private async Task NotifySignalCreatedAsync(TradeSignal signal, CancellationToken cancellationToken)
    {
        try
        {
            var binanceUrl = TradingLinkHelper.BuildBinanceUsdmFuturesUrl(signal.Symbol);
            await _notifications.PublishAsync(new NotificationCreateModel
            {
                Type = NotificationType.MarketSignalCreated,
                Severity = signal.Score >= 4 ? NotificationSeverity.Warning : NotificationSeverity.Info,
                Title = $"Tín hiệu {signal.Direction} {signal.Symbol}",
                Message = $"{signal.Interval} · Entry {signal.Entry:N4}, SL {signal.StopLoss:N4}, TP {signal.TakeProfit:N4}, RR {signal.RiskReward:N2}. {signal.Reason}",
                Source = "market_scan",
                SourceKey = $"{signal.Symbol}:{signal.Interval}:{signal.Direction}:{signal.CreatedAt:yyyyMMddHHmm}",
                RelatedSymbol = signal.Symbol,
                RelatedUrl = binanceUrl,
                PayloadJson = $$"""{"mmwCreateTradePath":"{{TradingLinkHelper.BuildMmwCreateTradePath(signal)}}","binanceUrl":"{{binanceUrl}}"}""",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Notification không được làm fail job scan.
            _logger.LogWarning(ex, "Failed to publish market signal notification for {SignalId} {Symbol}", signal.Id, signal.Symbol);
        }
    }

    private async Task<TradePreflightAnalysisResult> ReviewSignalAsync(TradeSignal signal, long? defaultAccountId, CancellationToken cancellationToken)
    {
        var account = defaultAccountId is long accountId
            ? await _accounts.FindAsync(accountId)
            : (await _accounts.FindListAsync(a => a.IsActive)).OrderBy(a => a.Id).FirstOrDefault();

        var quantity = 0m;
        if (account is not null)
        {
            var risk = await _settings.GetRiskSettingAsync(account.Id, cancellationToken);
            var stopDistance = Math.Abs(signal.Entry - signal.StopLoss);
            if (stopDistance > 0m && account.CurrentBalance > 0m)
            {
                var riskAmount = account.CurrentBalance * risk.MaxRiskPerTradePercent / 100m;
                quantity = Math.Round(riskAmount / stopDistance, 8, MidpointRounding.AwayFromZero);
            }
        }

        return await _preflightAnalysis.AnalyzeAsync(new TradePreflightAnalysisRequest
        {
            TradingAccountId = account?.Id ?? 0,
            Symbol = signal.Symbol,
            Direction = signal.Direction,
            OrderType = OrderType.Limit,
            Status = TradeStatus.Open,
            EntryPrice = signal.Entry,
            StopLoss = signal.StopLoss,
            TakeProfit = signal.TakeProfit,
            Quantity = quantity,
            Leverage = 20m,
            Note = $"MarketScan candidate. Score={signal.Score}. Reason={signal.Reason}",
        }, cancellationToken);
    }

    /// <summary>Áp SL/TP do AI đề xuất (đã validate đúng phía) vào đề xuất + tính lại RR.</summary>
    private static void ApplyAiLevels(TradeSignal signal, TradePreflightAnalysisResult review)
    {
        var changed = false;
        if (review.SuggestedStopLoss is decimal sl && sl > 0m)
        {
            signal.StopLoss = sl;
            changed = true;
        }
        if (review.SuggestedTakeProfit is decimal tp && tp > 0m)
        {
            signal.TakeProfit = tp;
            changed = true;
        }
        if (changed && signal.Entry > 0m)
        {
            var risk = Math.Abs(signal.Entry - signal.StopLoss);
            var reward = Math.Abs(signal.TakeProfit - signal.Entry);
            if (risk > 0m)
                signal.RiskReward = Math.Round(reward / risk, 4, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Ghép lý do gốc + nhãn quyết định AI + lời khuyên. Cap 500 (giới hạn cột Reason).</summary>
    private static string BuildReviewedReason(string originalReason, TradePreflightAnalysisResult review)
    {
        var tag = review.AiAnswered ? review.Decision.ToUpperInvariant() : "RULE-NỘI-BỘ";
        var advice = string.IsNullOrWhiteSpace(review.Advice) ? "(không có lời khuyên)" : review.Advice.Trim();

        var combined = $"{originalReason} | AI[{tag}]: {advice}";
        return combined.Length > 500 ? combined[..500] : combined;
    }

    private static bool TryBuildSuggestedSignal(
        string action,
        AiSignalResponse parsed,
        MarketAnalysis analysis,
        RiskSetting risk,
        out SuggestedSignal signal,
        out string rejectReason)
    {
        signal = default!;
        rejectReason = "";

        if (parsed.Entry is not > 0m || parsed.StopLoss is not > 0m || parsed.TakeProfit is not > 0m)
        {
            rejectReason = "AI không trả đủ entry/SL/TP.";
            return false;
        }

        var direction = action == "long" ? TradeDirection.Long : TradeDirection.Short;
        var entry = parsed.Entry.Value;
        var stopLoss = parsed.StopLoss.Value;
        var takeProfit = parsed.TakeProfit.Value;
        var isLong = direction == TradeDirection.Long;

        if (isLong && !(stopLoss < entry && entry < takeProfit))
        {
            rejectReason = "AI trả LONG nhưng SL/Entry/TP sai phía.";
            return false;
        }

        if (!isLong && !(takeProfit < entry && entry < stopLoss))
        {
            rejectReason = "AI trả SHORT nhưng TP/Entry/SL sai phía.";
            return false;
        }

        var riskDistance = Math.Abs(entry - stopLoss);
        var rewardDistance = Math.Abs(takeProfit - entry);
        if (riskDistance <= 0m)
        {
            rejectReason = "Khoảng cách rủi ro bằng 0.";
            return false;
        }

        var rr = Math.Round(rewardDistance / riskDistance, 4, MidpointRounding.AwayFromZero);
        if (rr < risk.MinRiskRewardRatio)
        {
            rejectReason = $"RR {rr:N2} thấp hơn policy {risk.MinRiskRewardRatio:N2}.";
            return false;
        }

        var reasonParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(parsed.Reason))
            reasonParts.Add("AI: " + parsed.Reason.Trim());
        if (!string.IsNullOrWhiteSpace(parsed.Invalidation))
            reasonParts.Add("Invalid: " + parsed.Invalidation.Trim());
        if (parsed.Warnings.Count > 0)
            reasonParts.Add("Warn: " + string.Join("; ", parsed.Warnings.Take(2)));

        var reason = string.Join(" | ", reasonParts);
        if (reason.Length > 500)
            reason = reason[..500];

        signal = new SuggestedSignal(
            direction,
            analysis.Bias,
            parsed.Score,
            Round8(entry),
            Round8(stopLoss),
            Round8(takeProfit),
            rr,
            reason);
        return true;
    }

    private static AiSignalResponse? ParseAiSignal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        foreach (var json in ExtractJsonCandidates(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = UnwrapAiRoot(doc.RootElement);
                if (root.ValueKind == JsonValueKind.String)
                    return ParseAiSignal(root.GetString());
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                var action = ReadString(root, "action", "decision", "side", "direction");
                var direction = ReadString(root, "direction", "side");
                if (string.Equals(action, "buy", StringComparison.OrdinalIgnoreCase))
                    action = "long";
                else if (string.Equals(action, "sell", StringComparison.OrdinalIgnoreCase))
                    action = "short";
                else if (string.Equals(action, "trade", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(direction))
                    action = direction;

                var response = new AiSignalResponse
                {
                    Action = action ?? "wait",
                    Score = ReadInt(root, "score", "rating") ?? 0,
                    Confidence = NormalizeConfidence(ReadDecimal(root, "confidence")),
                    Entry = ReadDecimal(root, "entry", "entryPrice", "price"),
                    StopLoss = ReadDecimal(root, "stopLoss", "sl", "suggestedStopLoss"),
                    TakeProfit = ReadDecimal(root, "takeProfit", "tp", "suggestedTakeProfit"),
                    RiskReward = ReadDecimal(root, "riskReward", "rr", "riskRewardRatio"),
                    Reason = ReadString(root, "reason", "rationale", "advice", "summary"),
                    Invalidation = ReadString(root, "invalidation", "invalidatedBy", "stopCondition"),
                    Warnings = ReadStringList(root, "warnings", "riskWarnings", "risks"),
                };

                return response;
            }
            catch
            {
                // Try next JSON candidate.
            }
        }

        return null;
    }

    private static string NormalizeAction(string? action)
    {
        return action?.Trim().ToLowerInvariant() switch
        {
            "long" or "buy" => "long",
            "short" or "sell" => "short",
            _ => "wait",
        };
    }

    private static JsonElement UnwrapAiRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return root;

        foreach (var name in new[] { "signal", "tradeSignal", "analysis", "result", "data", "response" })
        {
            if (TryGetProperty(root, name, out var nested) && nested.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                return nested;
        }

        return root;
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
                    .Take(3)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return [];

                return text.Split(['\n', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(3)
                    .ToList();
            }
        }

        return [];
    }

    private static decimal NormalizeConfidence(decimal? confidence)
    {
        if (confidence is null)
            return 0m;

        return confidence.Value > 1m ? confidence.Value / 100m : confidence.Value;
    }

    private static decimal Round8(decimal v) => Math.Round(v, 8, MidpointRounding.AwayFromZero);

    private static string? TrimRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Length > 1000 ? raw[..1000] : raw;
    }

    private sealed class AiSignalResponse
    {
        public string Action { get; set; } = "wait";
        public int Score { get; set; }
        public decimal Confidence { get; set; }
        public decimal? Entry { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public decimal? RiskReward { get; set; }
        public string? Reason { get; set; }
        public string? Invalidation { get; set; }
        public List<string> Warnings { get; set; } = [];
    }

    private static IndicatorRecord BuildHistory(WatchItem item, MarketAnalysis a, DateTime now) => new()
    {
        Symbol = item.Symbol,
        Interval = item.Interval,
        Price = a.Price,
        Rsi = a.Rsi,
        Ema20 = a.Ema20,
        Ema50 = a.Ema50,
        Macd = a.Macd,
        MacdSignal = a.MacdSignal,
        MacdHistogram = a.MacdHistogram,
        Atr = a.Atr,
        Bias = a.Bias,
        ScannedAt = now,
    };

    private static TradeSignal BuildSignal(WatchItem item, SuggestedSignal s, DateTime now) => new()
    {
        Symbol = item.Symbol,
        Interval = item.Interval,
        Direction = s.Direction,
        Bias = s.Bias,
        Score = s.Score,
        Entry = s.Entry,
        StopLoss = s.StopLoss,
        TakeProfit = s.TakeProfit,
        RiskReward = s.RiskReward,
        Reason = s.Reason,
        CreatedAt = now,
    };

    private async Task UpsertSnapshotAsync(WatchItem item, MarketAnalysis a, DateTime now)
    {
        var list = await _snapshots.FindListAsync(s => s.Symbol == item.Symbol && s.Interval == item.Interval);
        var snapshot = list.Count > 0 ? await _snapshots.FindAsync(list[0].Id) : null;

        var isNew = snapshot is null;
        snapshot ??= new MarketSnapshot { Symbol = item.Symbol, Interval = item.Interval };

        snapshot.Price = a.Price;
        snapshot.Rsi = a.Rsi;
        snapshot.Ema20 = a.Ema20;
        snapshot.Ema50 = a.Ema50;
        snapshot.Macd = a.Macd;
        snapshot.MacdSignal = a.MacdSignal;
        snapshot.MacdHistogram = a.MacdHistogram;
        snapshot.Atr = a.Atr;
        snapshot.Bias = a.Bias;
        snapshot.Notes = a.Notes;
        snapshot.UpdatedAt = now;

        if (isNew)
            await _snapshots.AddAsync(snapshot);
        else
            _snapshots.Update(snapshot);
    }
}
