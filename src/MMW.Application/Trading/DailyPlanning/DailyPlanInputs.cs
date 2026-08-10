using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

/// <summary>Cấu trúc giá đọc từ 20 phiên gần nhất. Bước 1 của thuật toán phân loại.</summary>
/// <remarks>
/// Tách khỏi <see cref="DayRegime"/> có chủ ý: <c>DayRegime</c> là NHÃN của cả ngày và có thể
/// là <c>HighVolatility</c> hay <c>EventDay</c> — những thứ không nói gì về cấu trúc giá. Trộn
/// hai khái niệm vào một enum sẽ làm mất thông tin cấu trúc đúng lúc cần nó nhất: những ngày
/// vừa có tin vừa đang trong xu hướng.
/// </remarks>
public enum DayStructure
{
    TrendUp = 1,
    TrendDown = 2,
    Range = 3,
}

/// <summary>Tên các thành phần đầu vào, dùng cho <c>MissingInputs</c> và hiển thị.</summary>
/// <remarks>
/// Là hằng số chứ không phải chuỗi rời rạc vì chúng đi vào cơ sở dữ liệu, ra giao diện, và
/// được test so khớp. Gõ tay ba nơi thì sớm muộn cũng lệch một nơi.
/// </remarks>
public static class DailyPlanInputNames
{
    public const string BtcStructure = "cấu trúc BTC";
    public const string AtrPercentile = "phân vị biến động";
    public const string KeyLevels = "mức giá tham chiếu";
    public const string FundingRate = "phí vốn";
    public const string OpenInterestChange = "biến động OI 24h";
    public const string LongShortRatio = "tỷ lệ mua/bán";
    public const string FearGreed = "chỉ số sợ hãi/tham lam";
}

/// <summary>
/// Toàn bộ đầu vào của việc phân loại ngày (FR-017).
/// </summary>
/// <remarks>
/// Các trường có thể null là CÓ CHỦ Ý: nguồn không khả dụng thì ghi vào <c>MissingInputs</c>
/// và kế hoạch vẫn sinh được. Bắt buộc mọi nguồn phải có mặt sẽ biến một lần sàn chậm mạng
/// thành một ngày không giao dịch được (FR-023).
/// </remarks>
public sealed record DailyPlanInputs
{
    /// <summary>Nến ngày BTC đã đóng. Cần ≥ 20 phiên cho cấu trúc, ≥ 74 phiên cho phân vị.</summary>
    public required IReadOnlyList<Candle> BtcDailyCandles { get; init; }

    /// <summary>Nến ngày của mã dùng để tính mức giá tham chiếu.</summary>
    public required IReadOnlyList<Candle> SymbolDailyCandles { get; init; }

    public required IReadOnlyList<ScheduledEvent> TodayEvents { get; init; }

    public decimal? FundingRate { get; init; }
    public decimal? OpenInterestChange24hPercent { get; init; }
    public decimal? LongShortAccountRatio { get; init; }
    public int? FearGreedIndex { get; init; }
}

/// <summary>Kết quả phân loại. Thuần, không tham chiếu cơ sở dữ liệu.</summary>
/// <remarks>
/// CẢNH BÁO: đừng so hai bản ghi bằng <c>==</c>. <see cref="MissingInputs"/> là
/// <c>IReadOnlyList</c> nên record so nó theo THAM CHIẾU — hai lần phân loại cùng đầu vào sẽ
/// cho "khác nhau" dù mọi giá trị đều trùng. So từng thành phần khi cần kiểm tính tất định.
/// </remarks>
public sealed record RegimeClassification(
    DayRegime Regime,
    VolatilityRegime Volatility,
    AllowedDirections AllowedDirections,
    decimal RiskMultiplier,
    int MaxTradesToday,
    string BtcStructure,
    decimal? AtrPercentile,
    IReadOnlyList<string> MissingInputs);

/// <summary>Các mức giá tham chiếu của ngày (FR-018).</summary>
public sealed record KeyLevels(
    decimal? PreviousDayHigh,
    decimal? PreviousDayLow,
    decimal? WeeklyOpen,
    decimal? DailyOpen)
{
    public static KeyLevels None { get; } = new(null, null, null, null);

    public bool IsEmpty => PreviousDayHigh is null && PreviousDayLow is null
                           && WeeklyOpen is null && DailyOpen is null;

    /// <summary>
    /// Tính từ chuỗi nến ngày đã đóng, cho ngày <paramref name="planDateUtc"/> sắp bắt đầu.
    /// </summary>
    /// <remarks>
    /// Thị trường tiền mã hoá chạy liên tục nên không có khoảng hở giữa hai phiên: giá mở của
    /// ngày kế tiếp bằng đúng giá đóng của phiên cuối. Vì vậy <c>DailyOpen</c> biết được ngay
    /// lúc 23:30 UTC, không phải chờ sang ngày.
    /// </remarks>
    public static KeyLevels From(IReadOnlyList<Candle> dailyCandles, DateOnly planDateUtc)
    {
        var closed = dailyCandles
            .Where(c => DateOnly.FromDateTime(c.OpenTime) < planDateUtc)
            .OrderBy(c => c.OpenTime)
            .ToList();

        if (closed.Count == 0) return None;

        var previous = closed[^1];

        // Tuần bắt đầu thứ Hai 00:00 UTC, trùng quy ước nến tuần của sàn.
        var weekStart = planDateUtc.AddDays(-(((int)planDateUtc.DayOfWeek + 6) % 7));

        var weeklyOpen = closed.FirstOrDefault(c => DateOnly.FromDateTime(c.OpenTime) == weekStart)?.Open
                         ?? (weekStart == planDateUtc ? previous.Close : null);

        return new KeyLevels(previous.High, previous.Low, weeklyOpen, previous.Close);
    }
}
