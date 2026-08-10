using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Cổng lấy dữ liệu thị trường công khai (không cần API key).
/// </summary>
public interface IMarketDataProvider
{
    Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <param name="interval">VD: "1m", "5m", "1h", "4h", "1d".</param>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy một trang nến bắt đầu từ <paramref name="startTimeUtc"/> để nạp kho lịch sử.
    /// Provider không hỗ trợ có thể giữ hành vi cũ; provider Binance phải cài phân trang thật.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetCandleHistoryAsync(
        string symbol,
        string interval,
        DateTime startTimeUtc,
        int limit = 1000,
        CancellationToken cancellationToken = default) =>
        GetCandlesAsync(symbol, interval, limit, cancellationToken);

    /// <summary>Lấy bước giá (tickSize) của symbol futures để làm tròn giá nhập. Null nếu không lấy được.</summary>
    Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────────────
    // Dữ liệu futures bổ sung (FR-003) — toàn bộ là endpoint công khai, không cần khoá.
    //
    // HỢP ĐỒNG LỖI, khác với ba phương thức phía trên: năm phương thức dưới đây trả `null`
    // khi không lấy được dữ liệu và KHÔNG ném ngoại lệ cho lỗi mạng hay lỗi phía sàn.
    //
    // Lý do: theo FR-006, thiếu dữ liệu ⟹ tiêu chí liên quan nhận 0 điểm và vòng chấm điểm
    // VẪN TIẾP TỤC. Nếu dùng ngoại lệ thì mỗi điểm gọi phải bọc try/catch, và chỉ cần quên
    // một chỗ là cả chu kỳ đánh giá chết vì một nguồn dữ liệu phụ. Trả `null` làm cho
    // hành vi đúng trở thành hành vi mặc định.
    //
    // NGOẠI LỆ DUY NHẤT: `period` không nằm trong danh sách trắng là lỗi LẬP TRÌNH, không
    // phải điều kiện dữ liệu, và PHẢI ném. Binance trả HTTP 200 kèm mảng rỗng khi `period`
    // sai (R-003 bẫy B1), nên để nó đi theo đường `null` sẽ biến một lỗi đánh máy thành
    // "thiếu dữ liệu ⟹ 0 điểm" và giết một tiêu chí vĩnh viễn trong im lặng.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Phí vốn hiện tại và giá đánh dấu.</summary>
    Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Một trang phí vốn đã thanh toán, tăng dần từ <paramref name="startTimeUtc"/>.</summary>
    Task<IReadOnlyList<FundingRatePoint>?> GetFundingHistoryAsync(
        string symbol,
        DateTime startTimeUtc,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FundingRatePoint>?>(null);

    /// <param name="period">Phải thuộc <see cref="FuturesDataPeriods.Allowed"/>, ngược lại ném <see cref="ArgumentException"/>.</param>
    Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken cancellationToken = default);

    /// <param name="period">Phải thuộc <see cref="FuturesDataPeriods.Allowed"/>.</param>
    Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken cancellationToken = default);

    /// <param name="limit">Trọng số rate-limit đo được: 2/5/10/20 ứng với limit 5/100/500/1000.</param>
    Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default);

    /// <param name="period">Phải thuộc <see cref="FuturesDataPeriods.Allowed"/>.</param>
    Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken cancellationToken = default);
}

/// <summary>
/// Danh sách trắng <c>period</c> của nhóm endpoint <c>/futures/data/*</c>, kiểm chứng từng
/// giá trị với API thật ở T001.
/// </summary>
public static class FuturesDataPeriods
{
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d" };

    /// <summary>Số ngày lịch sử tối đa của nhóm <c>/futures/data/*</c>. Xa hơn thì sàn trả <c>400 -1130</c>.</summary>
    public const int MaxHistoryDays = 30;

    /// <summary>Ném khi <paramref name="period"/> không hợp lệ. Đây là lỗi lập trình, không phải lỗi dữ liệu.</summary>
    public static void Validate(string period, string paramName = "period")
    {
        if (!Allowed.Contains(period))
        {
            throw new ArgumentException(
                $"period '{period}' không hợp lệ. Giá trị cho phép: {string.Join(", ", Allowed)}. " +
                "Binance trả HTTP 200 kèm mảng rỗng cho period sai, nên nếu không chặn ở đây thì " +
                "lỗi sẽ biến thành 'thiếu dữ liệu ⟹ 0 điểm' và không ai phát hiện.",
                paramName);
        }
    }
}
