namespace MMW.Application.MarketData.Models;

/// <summary>Ảnh chụp phí vốn và giá đánh dấu tại một thời điểm.</summary>
/// <param name="LastFundingRate">
/// Tỷ lệ phí vốn. Ở chế độ chạy thật đây là tỷ lệ DỰ PHÓNG cho kỳ thanh toán sắp tới;
/// ở chế độ kiểm thử lịch sử là tỷ lệ ĐÃ THANH TOÁN đọc từ kho. Khác biệt này là ngoại lệ
/// duy nhất được phép của nguyên tắc tương đương — xem data-model mục 9b.
/// </param>
public sealed record FundingSnapshot(
    decimal LastFundingRate,
    DateTime NextFundingTimeUtc,
    decimal MarkPrice,
    DateTime RetrievedAtUtc);

/// <summary>Một mốc thanh toán phí vốn trong lịch sử. Nguồn: <c>/fapi/v1/fundingRate</c>.</summary>
public sealed record FundingRatePoint(DateTime FundingTimeUtc, decimal FundingRate, decimal? MarkPrice);

public sealed record OpenInterestPoint(DateTime TimeUtc, decimal OpenInterest, decimal OpenInterestValue);

public sealed record OpenInterestSeries(string Symbol, string Period, IReadOnlyList<OpenInterestPoint> Points)
{
    /// <summary>
    /// Phần trăm thay đổi lượng hợp đồng mở trong <paramref name="window"/> gần nhất.
    /// Null khi không đủ dữ liệu phủ hết cửa sổ, hoặc khi giá trị gốc bằng 0.
    /// </summary>
    /// <remarks>
    /// Trả <c>null</c> chứ không trả 0 khi thiếu dữ liệu: 0 nghĩa là "không đổi", một kết luận
    /// thực sự về thị trường. Thiếu dữ liệu thì phải nói là thiếu, để tiêu chí liên quan
    /// nhận 0 điểm theo FR-006 thay vì nhận một kết luận bịa.
    /// </remarks>
    public decimal? ChangePercent(TimeSpan window)
    {
        if (Points.Count < 2) return null;

        var latest = Points[^1];
        var cutoff = latest.TimeUtc - window;

        // Điểm gần mốc cutoff nhất từ phía cũ. Không có điểm nào đủ cũ nghĩa là chuỗi
        // không phủ hết cửa sổ — trả null thay vì tính trên cửa sổ ngắn hơn yêu cầu.
        OpenInterestPoint? baseline = null;
        for (var i = Points.Count - 1; i >= 0; i--)
        {
            if (Points[i].TimeUtc > cutoff) continue;
            baseline = Points[i];
            break;
        }

        if (baseline is null || baseline.OpenInterest == 0m) return null;

        return (latest.OpenInterest - baseline.OpenInterest) / baseline.OpenInterest * 100m;
    }
}

public sealed record LongShortRatio(
    decimal LongShortRatioValue,
    decimal LongAccount,
    decimal ShortAccount,
    DateTime TimeUtc);

public sealed record DepthLevel(decimal Price, decimal Quantity);

public sealed record DepthSnapshot(
    IReadOnlyList<DepthLevel> Bids,
    IReadOnlyList<DepthLevel> Asks,
    DateTime RetrievedAtUtc)
{
    /// <summary>Chênh lệch mua-bán theo điểm cơ bản. Null khi một bên sổ lệnh rỗng.</summary>
    /// <remarks>
    /// Trả <c>null</c> thay vì 0 là có chủ ý, và khác với chữ ký trong bản hợp đồng đầu tiên.
    /// Sổ lệnh rỗng một bên nghĩa là symbol đang tạm dừng hoặc thanh khoản đã cạn. Trả 0
    /// ở đó nghĩa là "chênh lệch bằng 0" — điểm tuyệt đối — tức là chấm điểm CAO NHẤT
    /// đúng vào lúc thị trường tệ nhất. Null đẩy tiêu chí về 0 điểm theo FR-006, đúng chiều.
    /// </remarks>
    public decimal? SpreadBps
    {
        get
        {
            if (Bids.Count == 0 || Asks.Count == 0) return null;

            var bestBid = Bids[0].Price;
            var bestAsk = Asks[0].Price;
            var mid = (bestBid + bestAsk) / 2m;
            if (mid <= 0m) return null;

            return (bestAsk - bestBid) / mid * 10_000m;
        }
    }

    /// <summary>Tổng khối lượng nằm trong dải <paramref name="bps"/> điểm cơ bản quanh giá giữa.</summary>
    public decimal? DepthWithinBps(int bps, bool bidSide)
    {
        if (Bids.Count == 0 || Asks.Count == 0) return null;

        var mid = (Bids[0].Price + Asks[0].Price) / 2m;
        if (mid <= 0m) return null;

        var band = mid * bps / 10_000m;
        var levels = bidSide ? Bids : Asks;
        var limit = bidSide ? mid - band : mid + band;

        var total = 0m;
        foreach (var level in levels)
        {
            if (bidSide ? level.Price < limit : level.Price > limit) break;
            total += level.Quantity;
        }
        return total;
    }
}

public sealed record TakerFlow(decimal BuySellRatio, decimal BuyVolume, decimal SellVolume, DateTime TimeUtc);
