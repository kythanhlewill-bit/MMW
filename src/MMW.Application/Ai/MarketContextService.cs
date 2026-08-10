using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Abstractions;
using MMW.Application.Ai.Prompts;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Ai;

public interface IMarketContextService
{
    Task<IReadOnlyList<MarketContextRecord>> GetActiveAsync(string symbol, CancellationToken ct = default);
    Task<int> RunDailyBriefAsync(DailyPlan plan, CancellationToken ct = default);
    Task<int> ClassifyNewsAsync(CancellationToken ct = default);
}

public sealed class MarketContextService : IMarketContextService
{
    private readonly ILlmService _llm;
    private readonly IMacroEventProvider _headlines;
    private readonly IDailyBriefValidator _briefValidator;
    private readonly INewsClassifierValidator _newsValidator;
    private readonly IClock _clock;
    private readonly IBaseRepository<MarketContextRecord> _contexts;
    private readonly IBaseRepository<ScheduledEvent> _events;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<DailyPlan> _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MarketContextService> _logger;

    public MarketContextService(
        ILlmService llm, IMacroEventProvider headlines,
        IDailyBriefValidator briefValidator, INewsClassifierValidator newsValidator,
        IClock clock, IBaseRepository<MarketContextRecord> contexts,
        IBaseRepository<ScheduledEvent> events, IBaseRepository<EngineSetting> settings,
        IBaseRepository<DailyPlan> plans, IUnitOfWork unitOfWork,
        ILogger<MarketContextService> logger)
    {
        _llm = llm;
        _headlines = headlines;
        _briefValidator = briefValidator;
        _newsValidator = newsValidator;
        _clock = clock;
        _contexts = contexts;
        _events = events;
        _settings = settings;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MarketContextRecord>> GetActiveAsync(string symbol, CancellationToken ct = default) =>
        await _contexts.Queryable.AsNoTracking()
            .Where(x => x.ExpiresAtUtc > _clock.UtcNow)
            .ToListAsync(ct);

    public async Task<int> RunDailyBriefAsync(DailyPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.AiAnswered || !_llm.IsConfigured) return 0;

        var now = _clock.UtcNow;
        var setting = await _settings.Queryable.AsNoTracking()
            .FirstAsync(x => x.TradingAccountId == plan.TradingAccountId, ct);
        var calendar = await _events.Queryable.AsNoTracking()
            .Where(x => x.OccursAtUtc >= now && x.OccursAtUtc <= now.AddHours(48))
            .ToListAsync(ct);

        string? raw = null;
        try
        {
            var input = JsonSerializer.Serialize(new
            {
                providedCalendar = calendar.Select(x => new { x.Title, x.OccursAtUtc, x.DurationMinutes, x.Impact }),
                recentHeadlines = Array.Empty<string>(),
                marketStats = new { plan.DayRegime, plan.VolatilityRegime, plan.AtrPercentile, plan.FundingRate },
            });
            raw = await _llm.ChatAsync(DailyBriefPrompt.System, input, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Daily Brief AI không khả dụng cho kế hoạch {PlanId}.", plan.Id);
        }

        var result = _briefValidator.Validate(raw, calendar, now, setting);
        var trackedPlan = await _plans.FindAsync(plan.Id) ?? plan;
        trackedPlan.AiAnswered = result.Accepted;
        trackedPlan.AiDayRiskLevel = result.Accepted ? result.DayRiskLevel : null;
        trackedPlan.AiNarrative = result.Accepted ? result.Narrative : null;
        trackedPlan.AiConfidence = result.Accepted ? result.Confidence : null;
        _plans.Update(trackedPlan);

        await _contexts.AddAsync(new MarketContextRecord
        {
            Kind = MarketContextKind.DailyBrief,
            Severity = ContextSeverity.Noise,
            Leaning = MarketBias.Neutral,
            Narrative = result.Narrative,
            RecordedAtUtc = now,
            ExpiresAtUtc = now,
            SourceKey = $"daily-brief:{plan.Id}",
            RawResponseJson = raw,
            RejectedFields = JoinRejected(result.RejectedFields),
        });

        foreach (var window in result.ExtraBlackouts)
        {
            await _events.AddAsync(new ScheduledEvent
            {
                Kind = ScheduledEventKind.AiDetectedShock,
                Title = window.Reason,
                OccursAtUtc = window.FromUtc,
                DurationMinutes = (int)(window.ToUtc - window.FromUtc).TotalMinutes,
                Impact = window.Severity == ContextSeverity.High ? MacroEventImpact.High : MacroEventImpact.Medium,
                Origin = ScheduledEventOrigin.AiDetected,
                SourceKey = $"ai-brief:{plan.Id}:{window.FromUtc:O}",
                Notes = JoinRejected(result.RejectedFields),
            });
        }

        await _unitOfWork.CommitAsync(ct);
        CopyAiFields(trackedPlan, plan);
        return result.Accepted ? 1 : 0;
    }

    public async Task<int> ClassifyNewsAsync(CancellationToken ct = default)
    {
        if (!_llm.IsConfigured || !_headlines.IsConfigured) return 0;

        IReadOnlyList<Models.MacroEventModel> headlines;
        try
        {
            headlines = await _headlines.GetEventsAsync(_clock.UtcNow, TimeSpan.Zero, TimeSpan.FromHours(24), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được nguồn tin; bỏ qua lượt news-scan.");
            return 0;
        }

        var settings = await _settings.Queryable.AsNoTracking().ToListAsync(ct);
        if (settings.Count == 0) return 0;
        var watched = settings.SelectMany(x => x.SymbolList()).Distinct(StringComparer.Ordinal).ToList();
        var maxPerRun = Math.Max(0, settings.Min(x => x.AiMaxNewsCallsPerRun));
        var maxPerDay = Math.Max(0, settings.Min(x => x.AiMaxNewsCallsPerDay));
        var dayStart = _clock.UtcNow.Date;
        var calledToday = await _contexts.Queryable.AsNoTracking()
            .CountAsync(x => x.Kind == MarketContextKind.NewsItem && x.RecordedAtUtc >= dayStart, ct);
        var remaining = Math.Min(maxPerRun, Math.Max(0, maxPerDay - calledToday));
        if (remaining == 0) return 0;

        var keys = headlines.Select(x => x.SourceKey).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var existing = await _contexts.Queryable.AsNoTracking()
            .Where(x => x.SourceKey != null && keys.Contains(x.SourceKey))
            .Select(x => x.SourceKey!)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var headline in headlines.Where(x => !string.IsNullOrWhiteSpace(x.SourceKey) && !existingSet.Contains(x.SourceKey)).Take(remaining))
        {
            string? raw = null;
            try
            {
                raw = await _llm.ChatAsync(NewsClassifierPrompt.System,
                    JsonSerializer.Serialize(new { headline.SourceKey, headline.Title, headline.Summary, watchedSymbols = watched }), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "News Classifier lỗi tại {SourceKey}.", headline.SourceKey);
            }

            var result = _newsValidator.Validate(raw, watched, settings[0]);
            await _contexts.AddAsync(new MarketContextRecord
            {
                Kind = MarketContextKind.NewsItem,
                Severity = result.Severity,
                Leaning = result.Leaning,
                AffectedSymbols = string.Join(',', result.AffectedSymbols),
                Narrative = headline.Title,
                IsRumor = result.IsRumor,
                RecordedAtUtc = _clock.UtcNow,
                ExpiresAtUtc = result.Accepted ? _clock.UtcNow.AddMinutes(result.HalfLifeMinutes) : _clock.UtcNow,
                SourceKey = headline.SourceKey,
                RawResponseJson = raw,
                RejectedFields = JoinRejected(result.RejectedFields),
            });
            existingSet.Add(headline.SourceKey);
            written++;
        }

        if (written > 0) await _unitOfWork.CommitAsync(ct);
        return written;
    }

    private static string? JoinRejected(IReadOnlyList<string> fields) =>
        fields.Count == 0 ? null : string.Join("; ", fields);

    private static void CopyAiFields(DailyPlan source, DailyPlan target)
    {
        target.AiAnswered = source.AiAnswered;
        target.AiDayRiskLevel = source.AiDayRiskLevel;
        target.AiNarrative = source.AiNarrative;
        target.AiConfidence = source.AiConfidence;
    }
}
