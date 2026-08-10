using MMW.Application.Abstractions;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;

namespace MMW.Application.Backtest;

/// <summary>
/// Cổng dữ liệu thị trường của kiểm thử lịch sử. Cổng thứ hai và cuối cùng bị thay so với
/// chạy thật.
/// </summary>
/// <remarks>
/// Dòng lọc <c>CloseTime &lt;= clock.UtcNow</c> trong <see cref="GetCandlesAsync"/> là dòng
/// QUAN TRỌNG NHẤT của toàn bộ engine kiểm thử. Bỏ nó đi thì mọi con số kết quả đều đẹp và
/// đều vô nghĩa: thuật toán sẽ "biết" giá của tương lai mà không có triệu chứng nào lộ ra.
///
/// Bốn nguồn phái sinh trả <c>null</c> theo R-003 vì không dựng lại được từ dữ liệu công khai:
/// lượng hợp đồng mở chỉ có 30 ngày, tỷ lệ mua/bán và sổ lệnh không có lịch sử. Trả null đẩy
/// tiêu chí liên quan về 0 điểm theo FR-006 — đúng chiều, và số điểm mất đi được ghi rõ trong
/// <c>Limitations</c> của báo cáo.
/// </remarks>
public sealed class ArchiveMarketDataProvider : IMarketDataProvider, IMarketSentimentProvider
{
    private readonly IKlineArchiveReader _archive;
    private readonly IClock _clock;

    public ArchiveMarketDataProvider(IKlineArchiveReader archive, IClock clock)
    {
        _archive = archive;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var step = KlineArchiveService.IntervalSpan(interval);

        // Lấy dư một quãng rồi cắt, để `limit` đếm trên nến ĐÃ ĐÓNG chứ không đếm cả nến hở.
        var from = now - step * (limit + 2);

        var candles = await _archive.GetRangeAsync(symbol, interval, from, now + step, cancellationToken);

        return candles
            .Where(c => c.CloseTime <= now)   // ← dòng chống nhìn trước tương lai
            .TakeLast(limit)
            .ToList();
    }

    /// <summary>Giá đóng của cây nến 1 phút đã đóng gần nhất.</summary>
    /// <remarks>
    /// Dùng khung nhỏ nhất có trong kho để giá bám sát thời điểm mô phỏng nhất có thể. Không
    /// có nến 1 phút thì lùi dần sang khung lớn hơn — thà giá thô còn hơn ném và làm chết cả
    /// lần chạy.
    /// </remarks>
    public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        foreach (var interval in new[] { "1m", "5m", "15m", "1h", "4h", "1d" })
        {
            var candles = await GetCandlesAsync(symbol, interval, 1, cancellationToken);
            if (candles.Count > 0) return new Ticker(symbol, candles[^1].Close);
        }

        throw new InvalidOperationException(
            $"Kho nến không có dữ liệu nào của {symbol} tính đến {_clock.UtcNow:O}.");
    }

    public Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<SymbolPriceFilter?>(null);

    /// <summary>
    /// Phí vốn ĐÃ THANH TOÁN gần nhất, đọc từ kho lịch sử.
    /// </summary>
    /// <remarks>
    /// Đây là một XẤP XỈ và phải được ghi trong <c>Limitations</c>: chạy thật dùng tỷ lệ DỰ
    /// PHÓNG của chu kỳ đang chạy, còn ở đây là tỷ lệ đã chốt của chu kỳ trước. Hai con số gần
    /// nhau nhưng không bằng nhau, và đó là lý do <c>market.funding_crowding</c> là tiêu chí
    /// DUY NHẤT được loại khỏi phép so tương đương (SC-003).
    /// </remarks>
    public async Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var settled = await _archive.GetFundingAtAsync(symbol, now, cancellationToken);
        if (settled is null) return null;

        return new FundingSnapshot(
            settled.FundingRate,
            settled.FundingTimeUtc.AddHours(8),
            settled.MarkPrice ?? 0m,
            now);
    }

    // ── Bốn nguồn không dựng lại được từ dữ liệu công khai (R-003) ──────

    public Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<OpenInterestSeries?>(null);

    public Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<LongShortRatio?>(null);

    public Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<DepthSnapshot?>(null);

    public Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<TakerFlow?>(null);

    /// <summary>Không gọi mạng trong backtest; nguồn sentiment lịch sử không có trong kho.</summary>
    public Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default) =>
        Task.FromResult<int?>(null);
}
