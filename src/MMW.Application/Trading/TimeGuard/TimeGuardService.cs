using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Trading.TimeGuard;

public interface ITimeGuardService
{
    /// <summary>Nạp trước cửa sổ cho backtest; chạy thật không cần gọi.</summary>
    Task PreloadAsync(
        long tradingAccountId,
        IReadOnlyCollection<string> symbols,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Thời điểm này có được vào lệnh mới không.</summary>
    Task<BlackoutDecision> CheckAsync(long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Mọi cửa sổ chặn giao với <c>[from, to)</c>, đã hợp nhất phần chồng lấn (FR-012).</summary>
    Task<IReadOnlyList<BlackoutWindow>> GetWindowsAsync(long tradingAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Cửa sổ chặn kế tiếp BẮT ĐẦU trong vòng <paramref name="withinMinutes"/> (FR-013).</summary>
    Task<BlackoutWindow?> GetUpcomingAsync(long tradingAccountId, DateTime utcNow, int withinMinutes, CancellationToken ct = default);

    /// <summary>Phần lịch nạp tay còn hạn không (FR-014).</summary>
    Task<CalendarFreshness> GetCalendarFreshnessAsync(DateTime utcNow, CancellationToken ct = default);
}

/// <summary>
/// Tầng 2 của engine: tất định 100%, không AI, không mạng.
/// </summary>
/// <remarks>
/// Không có <c>DateTime.UtcNow</c> ở bất kỳ đâu trong lớp này — mọi thời điểm là THAM SỐ. Đó là
/// điều kiện để kiểm thử lịch sử chạy lại đúng cùng đoạn mã với chạy thật (R-001), và
/// <c>DeterminismGuardTests</c> quét mã IL để giữ điều đó.
/// </remarks>
public sealed class TimeGuardService : ITimeGuardService
{
    /// <summary>
    /// Trần độ dài một sự kiện, dùng để nới biên truy vấn. Sự kiện không có giờ cụ thể được nạp
    /// dưới dạng dài trọn ngày (ràng buộc 4 của contract), nên 1 ngày là đủ.
    /// </summary>
    private const int MaxEventDurationMinutes = 1440;

    /// <summary>Mã dùng khi câu hỏi không gắn với một cặp giao dịch cụ thể.</summary>
    private const string AnySymbol = "*";

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static readonly ScheduledEventKind[] RequiredCalendarKinds =
    [
        ScheduledEventKind.Cpi,
        ScheduledEventKind.Ppi,
        ScheduledEventKind.Pce,
        ScheduledEventKind.Nfp,
        ScheduledEventKind.FomcStatement,
        ScheduledEventKind.FomcPressConference,
    ];

    private readonly IScheduledEventCalendar _calendar;
    private readonly IDerivedEventGenerator _derived;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly ILogger<TimeGuardService> _logger;
    private readonly Dictionary<(long AccountId, string Symbol, DateOnly Date), IReadOnlyList<BlackoutWindow>> _dailyCache = new();
    private readonly Dictionary<(long AccountId, string Symbol), (DateTime From, DateTime To, IReadOnlyList<BlackoutWindow> Windows)> _rangeCache = new();

    public TimeGuardService(
        IScheduledEventCalendar calendar,
        IDerivedEventGenerator derived,
        IBaseRepository<EngineSetting> settings,
        ILogger<TimeGuardService> logger)
    {
        _calendar = calendar;
        _derived = derived;
        _settings = settings;
        _logger = logger;
    }

    public async Task<BlackoutDecision> CheckAsync(
        long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default)
    {
        IReadOnlyList<BlackoutWindow> windows;
        if (_rangeCache.TryGetValue((tradingAccountId, symbol), out var range)
            && utcNow >= range.From && utcNow < range.To)
        {
            windows = range.Windows;
        }
        else
        {
            var date = DateOnly.FromDateTime(utcNow);
            var key = (tradingAccountId, symbol, date);
            if (!_dailyCache.TryGetValue(key, out windows!))
            {
                var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                windows = await BuildWindowsAsync(tradingAccountId, dayStart, dayStart.AddDays(1), symbol, ct);
                _dailyCache[key] = windows;
            }
        }

        var blocking = windows.FirstOrDefault(w => w.BlocksNewEntries && w.Contains(utcNow));
        if (blocking is null) return BlackoutDecision.Allowed;

        // FR-015 — ghi vết dạng cấu trúc: loại, thời điểm sự kiện, biên cửa sổ, thời điểm đánh giá.
        _logger.LogDebug(
            "TimeGuard chặn vào lệnh mới. symbol={Symbol} accountId={AccountId} kind={Kind} " +
            "impact={Impact} eventAtUtc={EventAtUtc:o} windowFromUtc={WindowFromUtc:o} " +
            "windowToUtc={WindowToUtc:o} evaluatedAtUtc={EvaluatedAtUtc:o} title={Title}",
            symbol, tradingAccountId, blocking.Kind, blocking.Impact, blocking.EventAtUtc,
            blocking.FromUtc, blocking.ToUtc, utcNow, blocking.Title);

        return new BlackoutDecision(true, blocking, Describe(blocking));
    }

    public async Task PreloadAsync(
        long tradingAccountId,
        IReadOnlyCollection<string> symbols,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var windows = await BuildWindowsAsync(tradingAccountId, fromUtc, toUtc, symbol, ct);
            _rangeCache[(tradingAccountId, symbol)] = (fromUtc, toUtc, windows);
        }
    }

    public async Task<IReadOnlyList<BlackoutWindow>> GetWindowsAsync(
        long tradingAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await BuildWindowsAsync(tradingAccountId, fromUtc, toUtc, AnySymbol, ct);

    public async Task<BlackoutWindow?> GetUpcomingAsync(
        long tradingAccountId, DateTime utcNow, int withinMinutes, CancellationToken ct = default)
    {
        if (withinMinutes <= 0) return null;

        var horizon = utcNow.AddMinutes(withinMinutes);
        var windows = await BuildWindowsAsync(tradingAccountId, utcNow, horizon, AnySymbol, ct);

        // Chỉ lấy cửa sổ CHƯA bắt đầu. Cửa sổ đang diễn ra không phải "sắp tới" — trả về nó sẽ
        // làm lớp xử lý vị thế tưởng còn thời gian trong khi đã ở trong vùng cấm.
        return windows
            .Where(w => w.FromUtc > utcNow && w.FromUtc <= horizon)
            .OrderBy(w => w.FromUtc)
            .FirstOrDefault();
    }

    public async Task<CalendarFreshness> GetCalendarFreshnessAsync(
        DateTime utcNow, CancellationToken ct = default)
    {
        var lastByKind = await _calendar.GetLastSeededEventUtcByKindAsync(RequiredCalendarKinds, ct);
        var details = RequiredCalendarKinds
            .Select(kind =>
            {
                var found = lastByKind.TryGetValue(kind, out var last);
                return new CalendarKindFreshness(kind, found ? last : null, !found || last < utcNow);
            })
            .ToList();

        var stale = details.Where(d => d.IsStale).ToList();
        var coverageEnd = details.All(d => d.LastSeededEventUtc.HasValue)
            ? details.Min(d => d.LastSeededEventUtc!.Value)
            : (DateTime?)null;

        if (stale.Count == 0)
            return new CalendarFreshness(false, coverageEnd, null) { Kinds = details };

        var missing = stale.Where(d => d.LastSeededEventUtc is null).Select(d => KindName(d.Kind)).ToList();
        var expired = stale.Where(d => d.LastSeededEventUtc is not null)
            .Select(d => $"{KindName(d.Kind)} ({ToVietnamText(d.LastSeededEventUtc!.Value)})")
            .ToList();

        var problems = new List<string>();
        if (missing.Count > 0) problems.Add($"chưa có: {string.Join(", ", missing)}");
        if (expired.Count > 0) problems.Add($"đã hết hạn: {string.Join(", ", expired)}");

        return new CalendarFreshness(true, coverageEnd,
            $"Lịch kinh tế chưa phủ đủ theo từng loại ({string.Join("; ", problems)}). " +
            "Các mốc sinh bằng công thức vẫn hoạt động, nhưng lớp chặn của những loại trên " +
            "không còn đầy đủ cho đến khi nạp lịch mới.")
        {
            Kinds = details,
        };
    }

    private static string KindName(ScheduledEventKind kind) => kind switch
    {
        ScheduledEventKind.Cpi => "CPI",
        ScheduledEventKind.Ppi => "PPI",
        ScheduledEventKind.Pce => "PCE",
        ScheduledEventKind.Nfp => "NFP",
        ScheduledEventKind.FomcStatement => "FOMC statement",
        ScheduledEventKind.FomcPressConference => "FOMC họp báo",
        _ => kind.ToString(),
    };

    // ── Dựng cửa sổ ─────────────────────────────────────────────────────

    private async Task<IReadOnlyList<BlackoutWindow>> BuildWindowsAsync(
        long tradingAccountId, DateTime fromUtc, DateTime toUtc, string symbol, CancellationToken ct)
    {
        if (toUtc <= fromUtc) return Array.Empty<BlackoutWindow>();

        var setting = await _settings
            .Get(s => s.TradingAccountId == tradingAccountId)
            .Include(s => s.BlackoutRules)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Tài khoản {tradingAccountId} chưa có cấu hình engine (EngineSetting).");

        var rules = setting.BlackoutRules.ToDictionary(r => r.EventKind);

        // Sự kiện nằm ngoài khoảng hỏi vẫn có thể có cửa sổ tràn vào trong, nên phải nới biên
        // truy vấn ra hai phía.
        var pad = TimeSpan.FromMinutes(
            MaxEventDurationMinutes +
            (rules.Count == 0 ? 0 : rules.Values.Max(r => Math.Max(r.MinutesBefore, r.MinutesAfter))));

        var searchFrom = fromUtc - pad;
        var searchTo = toUtc + pad;

        var stored = await _calendar.GetBetweenAsync(searchFrom, searchTo, ct);
        var generated = _derived.Generate(searchFrom, searchTo, symbol);

        // Bản ghi trong cơ sở dữ liệu thắng bản sinh ra: nếu ai đó đã nạp tay một mốc phí vốn
        // với ghi chú riêng thì tôn trọng bản đó.
        var events = stored
            .Concat(generated)
            .GroupBy(e => e.SourceKey ?? $"{e.Kind}:{e.OccursAtUtc:O}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var windows = new List<BlackoutWindow>();
        foreach (var e in events)
        {
            var window = ToWindow(e, rules, setting, tradingAccountId);
            if (window is not null) windows.Add(window);
        }

        // Hợp nhất TRƯỚC rồi mới cắt theo khoảng hỏi. Làm ngược lại sẽ loại mất cửa sổ nằm
        // ngoài khoảng hỏi nhưng dính liền với cửa sổ bên trong, và khoảng hợp nhất trả về sẽ
        // ngắn hơn sự thật — hỏi lúc 14:15 mà nhận về "cấm từ 13:40" trong khi thực tế đã cấm
        // liên tục từ 12:30.
        return Merge(windows)
            .Where(w => w.FromUtc < toUtc && w.ToUtc > fromUtc)
            .ToList();
    }

    private BlackoutWindow? ToWindow(
        ScheduledEvent e,
        IReadOnlyDictionary<ScheduledEventKind, BlackoutRule> rules,
        EngineSetting setting,
        long tradingAccountId)
    {
        if (!rules.TryGetValue(e.Kind, out var rule))
        {
            // Không có luật thì không biết chặn rộng bao nhiêu. Bỏ qua nhưng phải KÊU LÊN —
            // im lặng ở đây nghĩa là một loại sự kiện biến mất khỏi lớp bảo vệ mà không ai biết.
            _logger.LogWarning(
                "Không có luật chặn cho loại sự kiện {Kind} ở tài khoản {AccountId}; " +
                "sự kiện {Title} lúc {OccursAtUtc:o} bị bỏ qua.",
                e.Kind, tradingAccountId, e.Title, e.OccursAtUtc);
            return null;
        }

        var from = e.OccursAtUtc.AddMinutes(-rule.MinutesBefore);
        var to = e.OccursAtUtc.AddMinutes((e.DurationMinutes ?? 0) + rule.MinutesAfter);

        // FR-011 — cửa sổ do AI đề xuất bị cắt về trần cấu hình. Cưỡng chế bằng SỐ HỌC ở phía
        // nhận: prompt có thể bị mô hình phớt lờ, phép cắt thì không. Cắt TRƯỚC khi hợp nhất,
        // nếu không một cửa sổ AI dài 20 tiếng sẽ nuốt các cửa sổ thật rồi kéo chúng biến mất
        // theo khi bị cắt.
        if (e.Origin == ScheduledEventOrigin.AiDetected)
        {
            var cap = from.AddMinutes(Math.Max(0, setting.AiBlackoutMaxMinutes));
            if (to > cap) to = cap;
        }

        if (to <= from) return null;

        return new BlackoutWindow(
            from, to, e.OccursAtUtc, e.Kind, e.Title, e.Impact,
            rule.RequiresPositionAction, rule.BlocksNewEntries);
    }

    /// <summary>
    /// Hợp nhất các cửa sổ chồng lấn hoặc chạm nhau thành một khoảng liên tục (FR-012).
    /// </summary>
    /// <remarks>
    /// Cửa sổ chạm nhau đúng biên cũng hợp nhất: với khoảng nửa mở thì
    /// <c>[a,b) ∪ [b,c) = [a,c)</c> — không hề có kẽ hở nào ở điểm nối, nên để lộ ra hai dòng
    /// là mô tả sai sự thật.
    /// </remarks>
    private static IReadOnlyList<BlackoutWindow> Merge(List<BlackoutWindow> windows)
    {
        if (windows.Count <= 1) return windows;

        var ordered = windows.OrderBy(w => w.FromUtc).ThenBy(w => w.ToUtc).ToList();
        var merged = new List<BlackoutWindow>();
        var group = new List<BlackoutWindow> { ordered[0] };
        var groupTo = ordered[0].ToUtc;

        foreach (var next in ordered.Skip(1))
        {
            if (next.FromUtc <= groupTo)
            {
                group.Add(next);
                if (next.ToUtc > groupTo) groupTo = next.ToUtc;
                continue;
            }

            merged.Add(Combine(group, groupTo));
            group = new List<BlackoutWindow> { next };
            groupTo = next.ToUtc;
        }

        merged.Add(Combine(group, groupTo));
        return merged;
    }

    private static BlackoutWindow Combine(List<BlackoutWindow> group, DateTime groupTo)
    {
        if (group.Count == 1) return group[0];

        // Sự kiện nặng nhất đại diện cho cả nhóm; cờ bảo vệ là phép HOẶC để hợp nhất không bao
        // giờ làm mất một lớp chặn (Nguyên tắc III).
        var lead = group.OrderByDescending(w => w.Impact).ThenBy(w => w.EventAtUtc).First();

        var title = string.Join(" + ", group
            .OrderBy(w => w.EventAtUtc)
            .Select(w => w.Title)
            .Distinct(StringComparer.Ordinal));

        return new BlackoutWindow(
            group.Min(w => w.FromUtc),
            groupTo,
            lead.EventAtUtc,
            lead.Kind,
            title,
            group.Max(w => w.Impact),
            group.Any(w => w.RequiresPositionAction),
            group.Any(w => w.BlocksNewEntries));
    }

    // ── Thông điệp tiếng Việt ───────────────────────────────────────────

    private static string Describe(BlackoutWindow w) =>
        $"Đang trong cửa sổ chặn \"{w.Title}\": từ {ToVietnamText(w.FromUtc)} đến " +
        $"{ToVietnamText(w.ToUtc)} (giờ Việt Nam).";

    /// <summary>
    /// Việt Nam ở UTC+7 cố định quanh năm, không có giờ mùa hè — nên cộng thẳng là đúng.
    /// Trader đọc giờ UTC sẽ phải tự cộng 7, và sẽ có lúc cộng nhầm.
    /// </summary>
    private static string ToVietnamText(DateTime utc) => (utc + VietnamOffset).ToString("HH:mm dd/MM");
}
