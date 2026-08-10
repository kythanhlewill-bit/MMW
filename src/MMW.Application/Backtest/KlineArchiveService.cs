using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Backtest;

/// <summary>
/// Phần CHỈ ĐỌC của kho lịch sử.
/// </summary>
/// <remarks>
/// Tách khỏi <see cref="IKlineArchiveService"/> vì một lý do cụ thể, không phải vì gọn: engine
/// kiểm thử đọc kho qua <c>ArchiveMarketDataProvider</c>, mà bản thân service nạp kho lại phụ
/// thuộc <c>IMarketDataProvider</c> để gọi sàn. Gộp làm một thì khi thay cổng dữ liệu ở chế độ
/// kiểm thử sẽ tạo vòng phụ thuộc.
///
/// Tách ra còn được thêm một tính chất đáng giá: đường đọc của kiểm thử KHÔNG THỂ chạm mạng,
/// vì nó không giữ tham chiếu nào tới sàn.
/// </remarks>
public interface IKlineArchiveReader
{
    /// <summary>Nạp trước các chuỗi cần cho một lần backtest để tránh truy vấn SQL theo từng nến.</summary>
    Task PreloadAsync(
        IReadOnlyCollection<string> symbols,
        IReadOnlyCollection<string> intervals,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default) => Task.CompletedTask;

    Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Các khoảng thiếu nến trong kho. Rỗng ⟹ dữ liệu liền mạch.</summary>
    Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Mốc phí vốn ĐÃ THANH TOÁN gần nhất tính đến <paramref name="atUtc"/>.</summary>
    Task<FundingRateArchive?> GetFundingAtAsync(string symbol, DateTime atUtc, CancellationToken ct = default);
}

/// <summary>Đọc kho, cộng thêm khả năng nạp bổ sung từ sàn.</summary>
public interface IKlineArchiveService : IKlineArchiveReader
{
    /// <summary>Nạp bổ sung từ sàn. Bất biến: nạp lại cùng khoảng không sinh bản ghi trùng (FR-005).</summary>
    Task<int> BackfillAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Nạp lịch sử phí vốn. Bất biến theo <c>(Symbol, FundingTimeUtc)</c>.</summary>
    Task<int> BackfillFundingAsync(string symbol, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

/// <summary>Đường đọc kho, không giữ tham chiếu nào tới sàn.</summary>
public sealed class KlineArchiveReader : IKlineArchiveReader
{
    private readonly IBaseRepository<KlineArchive> _klines;
    private readonly IBaseRepository<FundingRateArchive> _funding;
    private readonly Dictionary<(string Symbol, string Interval), List<Candle>> _candleCache = new();
    private readonly Dictionary<string, List<FundingRateArchive>> _fundingCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _cacheFromUtc;
    private DateTime? _cacheToUtc;

    public KlineArchiveReader(IBaseRepository<KlineArchive> klines, IBaseRepository<FundingRateArchive> funding)
    {
        _klines = klines;
        _funding = funding;
    }

    public async Task PreloadAsync(
        IReadOnlyCollection<string> symbols,
        IReadOnlyCollection<string> intervals,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        _candleCache.Clear();
        _fundingCache.Clear();
        _cacheFromUtc = fromUtc;
        _cacheToUtc = toUtc;

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var interval in intervals.Distinct(StringComparer.Ordinal))
            {
                _candleCache[(symbol, interval)] = await _klines
                    .Get(k => k.Symbol == symbol && k.Interval == interval
                              && k.OpenTimeUtc >= fromUtc && k.OpenTimeUtc < toUtc)
                    .AsNoTracking()
                    .OrderBy(k => k.OpenTimeUtc)
                    .Select(k => new Candle(k.OpenTimeUtc, k.Open, k.High, k.Low, k.Close, k.Volume, k.CloseTimeUtc))
                    .ToListAsync(ct);
            }

            _fundingCache[symbol] = await _funding
                .Get(f => f.Symbol == symbol && f.FundingTimeUtc >= fromUtc && f.FundingTimeUtc < toUtc)
                .AsNoTracking()
                .OrderBy(f => f.FundingTimeUtc)
                .ToListAsync(ct);
        }
    }

    public async Task<IReadOnlyList<Candle>> GetRangeAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return Array.Empty<Candle>();

        if (_cacheFromUtc <= fromUtc && _cacheToUtc >= toUtc
            && _candleCache.TryGetValue((symbol, interval), out var cached))
        {
            var start = LowerBound(cached, fromUtc, static x => x.OpenTime);
            var end = LowerBound(cached, toUtc, static x => x.OpenTime);
            return cached.GetRange(start, end - start);
        }

        return await _klines
            .Get(k => k.Symbol == symbol && k.Interval == interval
                      && k.OpenTimeUtc >= fromUtc && k.OpenTimeUtc < toUtc)
            .AsNoTracking()
            .OrderBy(k => k.OpenTimeUtc)
            .Select(k => new Candle(k.OpenTimeUtc, k.Open, k.High, k.Low, k.Close, k.Volume, k.CloseTimeUtc))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Tìm mọi khoảng thiếu nến bằng cách so chuỗi mốc mở thực tế với chuỗi lẽ ra phải có.
    /// </summary>
    /// <remarks>
    /// Phải gọi TRƯỚC mỗi lần chạy kiểm thử. Chạy trên dữ liệu khuyết cho ra kết quả trông hợp
    /// lệ nhưng sai — kiểu lỗi tệ nhất, vì không có gì để nghi ngờ.
    /// </remarks>
    public async Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return Array.Empty<(DateTime, DateTime)>();

        var step = KlineArchiveService.IntervalSpan(interval);

        var present = new HashSet<DateTime>(await _klines
            .Get(k => k.Symbol == symbol && k.Interval == interval
                      && k.OpenTimeUtc >= fromUtc && k.OpenTimeUtc < toUtc)
            .AsNoTracking()
            .Select(k => k.OpenTimeUtc)
            .ToListAsync(ct));

        var gaps = new List<(DateTime From, DateTime To)>();
        DateTime? gapStart = null;

        for (var t = KlineArchiveService.Align(fromUtc, step); t < toUtc; t += step)
        {
            if (present.Contains(t))
            {
                if (gapStart is not null)
                {
                    gaps.Add((gapStart.Value, t));
                    gapStart = null;
                }
                continue;
            }

            gapStart ??= t;
        }

        if (gapStart is not null) gaps.Add((gapStart.Value, toUtc));

        return gaps;
    }

    public async Task<FundingRateArchive?> GetFundingAtAsync(
        string symbol, DateTime atUtc, CancellationToken ct = default)
    {
        if (_cacheFromUtc <= atUtc && _cacheToUtc >= atUtc
            && _fundingCache.TryGetValue(symbol, out var cached))
        {
            var next = LowerBound(cached, atUtc.AddTicks(1), static x => x.FundingTimeUtc);
            return next == 0 ? null : cached[next - 1];
        }

        return await _funding
            .Get(f => f.Symbol == symbol && f.FundingTimeUtc <= atUtc)
            .AsNoTracking()
            .OrderByDescending(f => f.FundingTimeUtc)
            .FirstOrDefaultAsync(ct);
    }

    private static int LowerBound<T>(IReadOnlyList<T> items, DateTime value, Func<T, DateTime> key)
    {
        var lo = 0;
        var hi = items.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (key(items[mid]) < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

/// <summary>
/// Kho nến và kho phí vốn để kiểm thử lịch sử chạy được hoàn toàn offline.
/// </summary>
/// <remarks>
/// Nạp theo TRANG và bất biến theo khoá logic. Nạp lại chồng lấn là chuyện thường xuyên —
/// người ta chạy lại lệnh nạp sau khi bị ngắt giữa chừng — nên chống trùng phải là mặc định
/// chứ không phải một cờ tuỳ chọn.
/// </remarks>
public sealed class KlineArchiveService : IKlineArchiveService
{
    /// <summary>Trần một trang nến của Binance.</summary>
    private const int KlinePageSize = 1000;

    /// <summary>
    /// Trần một trang phí vốn. Là 500 chứ KHÔNG phải 1000: <c>limit=1001</c> trả HTTP 200 kèm
    /// phong bì lỗi phi tiêu chuẩn thay vì mảng (bẫy B3 ở R-003), và 500 là mức đã kiểm chứng.
    /// </summary>
    private const int FundingPageSize = 500;

    /// <summary>Chặn trên số trang mỗi lần nạp, để một tham số sai không quay vòng vô tận.</summary>
    private const int MaxPages = 500;

    private readonly IMarketDataProvider _marketData;
    private readonly IKlineArchiveReader _reader;
    private readonly IBaseRepository<KlineArchive> _klines;
    private readonly IBaseRepository<FundingRateArchive> _funding;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<KlineArchiveService> _logger;

    public KlineArchiveService(
        IMarketDataProvider marketData,
        IKlineArchiveReader reader,
        IBaseRepository<KlineArchive> klines,
        IBaseRepository<FundingRateArchive> funding,
        IUnitOfWork unitOfWork,
        ILogger<KlineArchiveService> logger)
    {
        _marketData = marketData;
        _reader = reader;
        _klines = klines;
        _funding = funding;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<IReadOnlyList<Candle>> GetRangeAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        _reader.GetRangeAsync(symbol, interval, fromUtc, toUtc, ct);

    public Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        _reader.FindGapsAsync(symbol, interval, fromUtc, toUtc, ct);

    public Task<FundingRateArchive?> GetFundingAtAsync(
        string symbol, DateTime atUtc, CancellationToken ct = default) =>
        _reader.GetFundingAtAsync(symbol, atUtc, ct);

    public async Task<int> BackfillAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return 0;

        var step = IntervalSpan(interval);
        var known = await ExistingOpenTimesAsync(symbol, interval, fromUtc, toUtc, ct);

        var saved = 0;
        var cursor = fromUtc;

        for (var page = 0; page < MaxPages && cursor < toUtc; page++)
        {
            var batch = await _marketData.GetCandleHistoryAsync(symbol, interval, cursor, KlinePageSize, ct);
            var fresh = batch
                .Where(c => c.OpenTime >= cursor && c.OpenTime < toUtc)
                .Where(c => known.Add(c.OpenTime))
                .Select(c => new KlineArchive
                {
                    Symbol = symbol,
                    Interval = interval,
                    OpenTimeUtc = c.OpenTime,
                    CloseTimeUtc = c.CloseTime,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume,
                })
                .ToList();

            if (fresh.Count > 0)
            {
                await _klines.AddRangeAsync(fresh);
                await _unitOfWork.CommitAsync(ct);
                saved += fresh.Count;
            }

            // Không tiến được nghĩa là sàn đã trả hết dữ liệu của khoảng này.
            var furthest = batch.Count > 0 ? batch.Max(c => c.OpenTime) : DateTime.MinValue;
            if (furthest < cursor) break;
            cursor = furthest + step;
        }

        _logger.LogInformation(
            "Nạp kho nến xong. symbol={Symbol} interval={Interval} from={From:o} to={To:o} saved={Saved}",
            symbol, interval, fromUtc, toUtc, saved);

        return saved;
    }

    public async Task<int> BackfillFundingAsync(
        string symbol, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) return 0;

        var known = new HashSet<DateTime>(await _funding
            .Get(f => f.Symbol == symbol && f.FundingTimeUtc >= fromUtc && f.FundingTimeUtc < toUtc)
            .AsNoTracking()
            .Select(f => f.FundingTimeUtc)
            .ToListAsync(ct));

        var saved = 0;
        var cursor = fromUtc;

        for (var page = 0; page < MaxPages && cursor < toUtc; page++)
        {
            var batch = await _marketData.GetFundingHistoryAsync(symbol, cursor, FundingPageSize, ct);
            if (batch is null || batch.Count == 0) break;

            var fresh = batch
                .Where(x => x.FundingTimeUtc >= cursor && x.FundingTimeUtc < toUtc)
                .Where(x => known.Add(x.FundingTimeUtc))
                .Select(x => new FundingRateArchive
                {
                    Symbol = symbol,
                    FundingTimeUtc = x.FundingTimeUtc,
                    FundingRate = x.FundingRate,
                    MarkPrice = x.MarkPrice,
                })
                .ToList();

            if (fresh.Count > 0)
            {
                await _funding.AddRangeAsync(fresh);
                await _unitOfWork.CommitAsync(ct);
                saved += fresh.Count;
            }

            var furthest = batch.Max(x => x.FundingTimeUtc);
            if (furthest < cursor) break;
            cursor = furthest.AddMilliseconds(1);
        }

        _logger.LogInformation(
            "Nạp kho phí vốn xong. symbol={Symbol} from={From:o} to={To:o} saved={Saved}",
            symbol, fromUtc, toUtc, saved);

        return saved;
    }

    // ── Trợ giúp ────────────────────────────────────────────────────────

    private async Task<HashSet<DateTime>> ExistingOpenTimesAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        new(await _klines
            .Get(k => k.Symbol == symbol && k.Interval == interval
                      && k.OpenTimeUtc >= fromUtc && k.OpenTimeUtc < toUtc)
            .AsNoTracking()
            .Select(k => k.OpenTimeUtc)
            .ToListAsync(ct));

    /// <summary>Độ dài một cây nến. Ném với khung lạ — đoán bừa sẽ làm phép dò khoảng thiếu sai im lặng.</summary>
    public static TimeSpan IntervalSpan(string interval) => interval switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "3m" => TimeSpan.FromMinutes(3),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "2h" => TimeSpan.FromHours(2),
        "4h" => TimeSpan.FromHours(4),
        "6h" => TimeSpan.FromHours(6),
        "12h" => TimeSpan.FromHours(12),
        "1d" => TimeSpan.FromDays(1),
        _ => throw new ArgumentException($"Khung thời gian '{interval}' không hỗ trợ.", nameof(interval)),
    };

    /// <summary>Làm tròn xuống mốc mở nến gần nhất, để chuỗi lý thuyết khớp mốc thật của sàn.</summary>
    internal static DateTime Align(DateTime value, TimeSpan step) =>
        new(value.Ticks - value.Ticks % step.Ticks, DateTimeKind.Utc);

}
