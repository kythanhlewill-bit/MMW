using Microsoft.EntityFrameworkCore;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Trading.TimeGuard;

/// <summary>Cuốn lịch sự kiện nạp tay và do AI phát hiện.</summary>
public interface IScheduledEventCalendar
{
    /// <summary>Sự kiện có mốc trong nửa khoảng <c>[fromUtc, toUtc)</c>.</summary>
    Task<IReadOnlyList<ScheduledEvent>> GetBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>
    /// Mốc NẠP/SINH LỊCH cuối cùng của từng loại được yêu cầu. Loại chưa có dữ liệu không xuất
    /// hiện trong kết quả; caller phải coi nó là thiếu (FR-014).
    /// </summary>
    Task<IReadOnlyDictionary<ScheduledEventKind, DateTime>> GetLastSeededEventUtcByKindAsync(
        IReadOnlyCollection<ScheduledEventKind> kinds,
        CancellationToken ct = default);

    /// <summary>Nạp sự kiện. Bất biến theo <c>SourceKey</c>: nạp lại cùng tệp không sinh bản ghi trùng.</summary>
    Task<int> ImportAsync(IEnumerable<ScheduledEvent> events, CancellationToken ct = default);
}

public sealed class ScheduledEventCalendar : IScheduledEventCalendar
{
    private readonly IBaseRepository<ScheduledEvent> _events;
    private readonly IUnitOfWork _unitOfWork;

    public ScheduledEventCalendar(IBaseRepository<ScheduledEvent> events, IUnitOfWork unitOfWork)
    {
        _events = events;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ScheduledEvent>> GetBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return Array.Empty<ScheduledEvent>();

        return await _events
            .Get(e => e.OccursAtUtc >= fromUtc && e.OccursAtUtc < toUtc)
            .AsNoTracking()
            .OrderBy(e => e.OccursAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<ScheduledEventKind, DateTime>> GetLastSeededEventUtcByKindAsync(
        IReadOnlyCollection<ScheduledEventKind> kinds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count == 0)
            return new Dictionary<ScheduledEventKind, DateTime>();

        var requested = kinds.Distinct().ToArray();

        // Chỉ lấy dữ liệu được seeder/import nạp. Các mốc Derived như funding có sẵn đến vô
        // tận; tính chúng vào freshness sẽ làm lịch kinh tế vĩnh viễn báo xanh.
        var rows = await _events
            .Get(e => e.Origin == ScheduledEventOrigin.Seeded && requested.Contains(e.Kind))
            .AsNoTracking()
            .Select(e => new { e.Kind, e.OccursAtUtc })
            .ToListAsync(ct);

        // Gom trong bộ nhớ để cùng chạy ổn định trên SQL Server lẫn provider InMemory dùng
        // trong test; bảng lịch rất nhỏ nên không có rủi ro tải dữ liệu lớn.
        return rows
            .GroupBy(e => e.Kind)
            .ToDictionary(g => g.Key, g => g.Max(e => e.OccursAtUtc));
    }

    public async Task<int> ImportAsync(IEnumerable<ScheduledEvent> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var incoming = events.ToList();

        var missingKey = incoming.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.SourceKey));
        if (missingKey is not null)
        {
            // Không có khoá thì không thể nạp lại mà không trùng, và một cửa sổ chặn bị đếm
            // đôi sẽ lặng lẽ nở rộng mỗi lần chạy seeder.
            throw new ArgumentException(
                $"Sự kiện '{missingKey.Title}' ({missingKey.OccursAtUtc:O}) thiếu SourceKey — " +
                "không nạp được vì sẽ không chống được trùng.", nameof(events));
        }

        // Trùng ngay trong lô nạp cũng phải loại, không chỉ trùng với dữ liệu đã có.
        var deduped = incoming
            .GroupBy(e => e.SourceKey!, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var keys = deduped.Select(e => e.SourceKey!).ToList();
        var existing = await _events
            .Get(e => e.SourceKey != null && keys.Contains(e.SourceKey))
            .AsNoTracking()
            .Select(e => e.SourceKey!)
            .ToListAsync(ct);

        var known = new HashSet<string>(existing, StringComparer.Ordinal);
        var fresh = deduped.Where(e => !known.Contains(e.SourceKey!)).ToList();
        if (fresh.Count == 0) return 0;

        await _events.AddRangeAsync(fresh);
        await _unitOfWork.CommitAsync(ct);

        return fresh.Count;
    }
}
