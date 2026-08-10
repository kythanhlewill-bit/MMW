using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Backtest.Models;

public sealed record BacktestRequest(
    string Name,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<string> Symbols,
    long TradingAccountId,
    EngineSetting? SettingsOverride = null,
    bool PersistScorecards = false,
    bool CollectTelemetry = true);

public sealed record HourStats(int TradeCount, decimal WinRate, decimal ExpectancyR);

public sealed record RegimeStats(int TradeCount, decimal WinRate, decimal ExpectancyR);

/// <summary>Khoảng tin cậy hai phía 95%.</summary>
public sealed record ConfidenceInterval(decimal Lower, decimal Upper);

/// <summary>Thống kê của một nhóm lệnh, kèm độ bất định thay vì chỉ một con số điểm.</summary>
public sealed record TradeGroupStats(
    int TradeCount,
    decimal WinRate,
    decimal ExpectancyR,
    ConfidenceInterval WinRate95,
    ConfidenceInterval ExpectancyR95)
{
    /// <summary>Điều kiện mẫu tối thiểu của Adaptive Execution V2 §9.5.</summary>
    public bool HasMinimumSample => TradeCount >= 100;
}

/// <summary>Số cơ hội còn lại nếu đặt rào R:R tại một mức cụ thể.</summary>
public sealed record StructuralRrThresholdStats(
    decimal ThresholdR,
    int EligibleCount,
    decimal PercentOfObserved,
    decimal PercentOfEvaluated);

/// <summary>
/// Phân phối R:R cấu trúc của TOÀN BỘ lượt dựng được mức, không chỉ phần đuôi đã qua rào hiện tại.
/// </summary>
public sealed record StructuralRrDistribution(
    int EvaluatedCount,
    int ObservedCount,
    int InsufficientRoomVetoCount,
    int UnplannableStopCount,
    decimal? Minimum,
    decimal? P10,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal? P90,
    decimal? Maximum,
    IReadOnlyList<StructuralRrThresholdStats> Thresholds);

/// <param name="TotalFees">Phí giao dịch theo % khối lượng danh nghĩa (giữ để so với các lần chạy cũ).</param>
/// <param name="TotalSlippage">Trượt giá theo đơn vị giá (giữ để so với các lần chạy cũ).</param>
/// <param name="TotalFeeR">Phí giao dịch quy ra R — con số duy nhất so sánh được giữa các mã.</param>
/// <param name="TotalFundingR">Phí vốn quy ra R. Dương = tiền ra.</param>
/// <param name="TotalSlippageR">Trượt giá quy ra R.</param>
/// <param name="Limitations">KHÔNG ĐƯỢC rỗng — xem <see cref="BacktestLimitations"/>.</param>
/// <remarks>
/// Ba trường <c>…R</c> tồn tại vì % và đơn vị giá KHÔNG cộng được giữa các mã: 0,04% của BTC
/// và 0,04% của một mã giá thấp là hai khoản tiền khác hẳn nhau, và cùng một khoản phí ăn vào
/// R nhiều hay ít còn tuỳ dừng lỗ rộng bao nhiêu. Hai trường cũ được giữ nguyên nghĩa để các
/// lần chạy trước vẫn đọc được — không đổi nghĩa một cột đã có dữ liệu.
/// </remarks>
public sealed record BacktestReport(
    long RunId,
    int TradeCount,
    decimal WinRate,
    decimal ExpectancyR,
    decimal MaxDrawdownPercent,
    int LongestLossStreak,
    decimal TotalFees,
    decimal TotalSlippage,
    IReadOnlyDictionary<int, HourStats> ByHourUtc,
    IReadOnlyDictionary<DayRegime, RegimeStats> ByRegime,
    IReadOnlyList<string> Limitations,
    decimal TotalFeeR = 0m,
    decimal TotalFundingR = 0m,
    decimal TotalSlippageR = 0m,
    decimal MakerFeeR = 0m,
    decimal TakerFeeR = 0m,
    int LimitTranchesOffered = 0,
    int LimitTranchesFilled = 0,
    int LimitTranchesExpired = 0,
    ConfidenceInterval? WinRate95 = null,
    ConfidenceInterval? ExpectancyR95 = null,
    IReadOnlyDictionary<string, TradeGroupStats>? ByMode = null,
    IReadOnlyDictionary<BacktestExitReason, TradeGroupStats>? ByExitReason = null,
    StructuralRrDistribution? StructuralRr = null,
    int ComparableTrialNumber = 1,
    int DirectionMarginMaterialBlocks = 0,
    TradingStrategyVersion StrategyVersion = TradingStrategyVersion.AdaptiveV2,
    decimal GrossExpectancyR = 0m,
    BacktestTelemetryReport? Telemetry = null)
{
    /// <summary>Tỉ lệ chân limit khớp được, %. Thấp ⟹ so sánh với V1 không còn công bằng.</summary>
    public decimal LimitFillRatePercent => LimitTranchesOffered == 0
        ? 0m
        : (decimal)LimitTranchesFilled / LimitTranchesOffered * 100m;

    public decimal LimitExpiryRatePercent => LimitTranchesOffered == 0
        ? 0m
        : (decimal)LimitTranchesExpired / LimitTranchesOffered * 100m;

    /// <summary>Phần phí trả theo biểu maker, %. Đo trực tiếp lợi ích của việc chuyển sang limit.</summary>
    public decimal MakerFeeSharePercent => TotalFeeR == 0m ? 0m : MakerFeeR / TotalFeeR * 100m;
}

/// <summary>Các phép thống kê dùng chung cho báo cáo; không phụ thuộc DB hay engine.</summary>
public static class BacktestStatistics
{
    private const double Z95 = 1.959963984540054;

    /// <summary>Khoảng Wilson cho tỉ lệ nhị thức, trả về đơn vị phần trăm.</summary>
    public static ConfidenceInterval WinRate95(int wins, int count)
    {
        if (count <= 0) return new ConfidenceInterval(0m, 0m);

        var n = (double)count;
        var p = (double)wins / n;
        var z2 = Z95 * Z95;
        var denominator = 1d + z2 / n;
        var centre = (p + z2 / (2d * n)) / denominator;
        var radius = Z95 * Math.Sqrt((p * (1d - p) + z2 / (4d * n)) / n) / denominator;

        return new ConfidenceInterval(
            (decimal)Math.Max(0d, centre - radius) * 100m,
            (decimal)Math.Min(1d, centre + radius) * 100m);
    }

    /// <summary>Khoảng tin cậy của trung bình theo xấp xỉ chuẩn.</summary>
    public static ConfidenceInterval Mean95(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return new ConfidenceInterval(0m, 0m);

        var mean = values.Average();
        if (values.Count == 1) return new ConfidenceInterval(mean, mean);

        var variance = values.Sum(x => (x - mean) * (x - mean)) / (values.Count - 1);
        var standardError = Math.Sqrt((double)variance / values.Count);
        var margin = (decimal)(Z95 * standardError);
        return new ConfidenceInterval(mean - margin, mean + margin);
    }

    public static TradeGroupStats Group(IReadOnlyList<SimulatedTradePosition> trades)
    {
        if (trades.Count == 0)
            return new TradeGroupStats(0, 0m, 0m, WinRate95(0, 0), Mean95(Array.Empty<decimal>()));

        var wins = trades.Count(t => t.Outcome == TradeOutcome.Win);
        var values = trades.Select(t => t.RMultiple).ToList();
        return new TradeGroupStats(
            trades.Count,
            (decimal)wins / trades.Count * 100m,
            values.Average(),
            WinRate95(wins, trades.Count),
            Mean95(values));
    }

    public static StructuralRrDistribution StructuralRr(
        int evaluatedCount,
        IReadOnlyList<decimal> observed,
        int insufficientRoomVetoCount,
        int unplannableStopCount)
    {
        var sorted = observed.OrderBy(x => x).ToArray();
        var thresholds = new[] { 0.50m, 0.75m, 1.00m, 1.10m, 1.20m, 1.30m, 1.40m, 1.50m, 1.60m, 1.80m, 2.00m }
            .Select(threshold =>
            {
                var eligible = sorted.Count(x => x >= threshold);
                return new StructuralRrThresholdStats(
                    threshold,
                    eligible,
                    sorted.Length == 0 ? 0m : (decimal)eligible / sorted.Length * 100m,
                    evaluatedCount == 0 ? 0m : (decimal)eligible / evaluatedCount * 100m);
            })
            .ToList();

        return new StructuralRrDistribution(
            evaluatedCount,
            sorted.Length,
            insufficientRoomVetoCount,
            unplannableStopCount,
            Quantile(sorted, 0m),
            Quantile(sorted, 0.10m),
            Quantile(sorted, 0.25m),
            Quantile(sorted, 0.50m),
            Quantile(sorted, 0.75m),
            Quantile(sorted, 0.90m),
            Quantile(sorted, 1m),
            thresholds);
    }

    private static decimal? Quantile(IReadOnlyList<decimal> sorted, decimal probability)
    {
        if (sorted.Count == 0) return null;
        if (sorted.Count == 1) return sorted[0];

        var position = probability * (sorted.Count - 1);
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper) return sorted[lower];

        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}

/// <summary>
/// Sinh danh sách hạn chế của một lần chạy.
/// </summary>
/// <remarks>
/// Một báo cáo kiểm thử không nêu hạn chế của chính nó sẽ được đọc như một lời hứa — và đó
/// chính là cách người ta thuyết phục bản thân bật giao dịch thật quá sớm. Vì vậy danh sách
/// này được SINH TỰ ĐỘNG chứ không do người viết báo cáo nhớ điền, và có test khẳng định nó
/// không bao giờ rỗng.
/// </remarks>
public static class BacktestLimitations
{
    /// <summary>
    /// Các tiêu chí luôn nhận 0 điểm khi chạy lịch sử, vì nguồn dữ liệu của chúng không dựng
    /// lại được (R-003). Tổng điểm mất: 10/100.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> UnavailableCriteria =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["liquidity.open_interest"] = 5,
            ["liquidity.spread_depth"] = 5,
        };

    /// <summary>
    /// Tiêu chí được phép LỆCH giữa kiểm thử và chạy thật, và chỉ đúng một.
    /// </summary>
    /// <remarks>
    /// Chạy thật đọc tỷ lệ phí vốn DỰ PHÓNG của chu kỳ đang chạy; kiểm thử đọc tỷ lệ ĐÃ THANH
    /// TOÁN của chu kỳ trước. Danh sách phải TƯỜNG MINH theo khoá — nới lỏng phép so sánh để
    /// "cho qua sai số" sẽ che luôn những lệch thật sự do lỗi mã.
    /// </remarks>
    public static readonly IReadOnlySet<string> ParityExclusions =
        new HashSet<string>(StringComparer.Ordinal) { "market.funding_crowding" };

    public static IReadOnlyList<string> Build(
        decimal takerFeePercent,
        decimal entrySlippageBps,
        decimal stopSlippageBps,
        int missingCandles,
        int fundingSettlements = 0,
        decimal makerFeePercent = 0m,
        bool limitFillRequiresThrough = true,
        int limitTranchesOffered = 0,
        int limitTranchesFilled = 0,
        int limitTranchesExpired = 0)
    {
        var lost = UnavailableCriteria.Values.Sum();

        var items = new List<string>
        {
            $"Mất {lost}/100 điểm: {string.Join(", ", UnavailableCriteria.Select(kv => $"{kv.Key} ({kv.Value}đ)"))} " +
            "luôn nhận 0 điểm vì lượng hợp đồng mở chỉ có 30 ngày lịch sử và sổ lệnh không có lịch sử công khai.",

            "Phí vốn dùng tỷ lệ ĐÃ THANH TOÁN của chu kỳ trước, trong khi chạy thật dùng tỷ lệ DỰ PHÓNG " +
            "của chu kỳ đang chạy — hai con số gần nhau nhưng không bằng nhau.",

            "Khi một cây nến chạm cả dừng lỗ lẫn chốt lời, giả định DỪNG LỖ KHỚP TRƯỚC. Dữ liệu nến " +
            "không cho biết giá chạm mức nào trước; giả định ngược lại sẽ thổi phồng kết quả có hệ thống.",

            $"Phí theo LOẠI LỆNH của từng chân: thị trường (chân đầu của trend mạnh, dừng lỗ, " +
            $"time-stop, đóng cuối kỳ) " +
            $"chịu taker {takerFeePercent:N2}% kèm trượt giá — vào lệnh {entrySlippageBps:N1} và dừng lỗ " +
            $"{stopSlippageBps:N1} điểm cơ bản; chân limit (vào lệnh bổ sung, chốt lời) chịu maker " +
            $"{makerFeePercent:N2}% và KHÔNG trượt giá, vì lệnh chờ sẵn khớp đúng mức đã đặt hoặc tốt hơn.",

            limitFillRequiresThrough
                ? "Mô hình khớp limit: THẬN TRỌNG — giá phải đi XUYÊN QUA mức mới tính khớp, coi như " +
                  "luôn phải đợi hết phần xếp trước trong hàng đợi. Chạy lại với mô hình lạc quan " +
                  "(chạm là khớp) để biết kết quả có phụ thuộc giả định này không."
                : "Mô hình khớp limit: LẠC QUAN — chạm mức là tính khớp, coi như lệnh luôn đứng đầu " +
                  "hàng đợi. Đây là biên TRÊN của kết quả; phải đối chiếu với mô hình thận trọng.",

            $"ĐÃ trừ phí vốn khi giữ vị thế qua mốc thanh toán 00:00/08:00/16:00 UTC " +
            $"({fundingSettlements} lượt thanh toán trong lần chạy này). Tỷ lệ dùng là tỷ lệ đã " +
            "thanh toán tại đúng mốc đó; giá đánh dấu lấy từ kho, thiếu thì lùi về giá đóng cửa nến.",

            "Khối lượng mỗi tranche tính theo NGÂN SÁCH RỦI RO chia cho khoảng cách riêng của nó " +
            "tới dừng lỗ ban đầu. Khớp đủ mọi tranche rồi dừng lỗ mất đúng ngân sách; khớp một " +
            "phần chỉ mất phần ngân sách đã khớp — nên số lệnh scale-in và lệnh một điểm vào so " +
            "sánh được với nhau theo R.",

            "Range/Standard bắt đầu bằng LỆNH CHỜ; setup chưa khớp không được tính là giao dịch, " +
            "không chịu phí/funding và không tạo exposure tương quan, nhưng vẫn chặn một lệnh chờ " +
            "trùng cùng mã. Range hết hạn sau 8 nến, Standard theo LimitEntryExpiryBars.",

            "Dừng theo thời gian: sau TimeStopBars nến kể từ lần khớp đầu mà MFE chưa từng đạt " +
            "TimeStopMinR thì đóng market. Sau TP1, stop hoà vốn bù phí đã trả + phí stop dự kiến + " +
            "đệm 0,05R; trailing pivot chỉ có hiệu lực từ cây nến kế tiếp để tránh nhìn trước.",
        };

        if (limitTranchesOffered > 0)
        {
            var fillRate = (decimal)limitTranchesFilled / limitTranchesOffered * 100m;
            var expiryRate = (decimal)limitTranchesExpired / limitTranchesOffered * 100m;

            items.Add(
                $"Chân limit: đặt {limitTranchesOffered}, khớp {limitTranchesFilled} ({fillRate:N1}%), " +
                $"hết hạn chưa khớp {limitTranchesExpired} ({expiryRate:N1}%). Phần còn lại bị huỷ do " +
                "lệnh đã chốt lời hoặc dừng lỗ trước.");

            // Tỉ lệ khớp thấp nghĩa là phần lớn kế hoạch không bao giờ chạy như đã viết, và
            // so sánh với một phiên bản vào lệnh bằng lệnh thị trường không còn cùng một thứ.
            if (fillRate < 60m)
                items.Add(
                    $"⚠️ Chỉ {fillRate:N1}% chân limit khớp được. Dưới 60% thì so sánh trực tiếp với " +
                    "phiên bản vào lệnh bằng lệnh thị trường KHÔNG còn công bằng — hai bên đang chạy " +
                    "hai kế hoạch khác nhau, không phải cùng một kế hoạch với chi phí khác nhau.");
        }

        if (missingCandles > 0)
            items.Add($"Kho nến thiếu {missingCandles} cây trong khoảng chạy.");

        return items;
    }
}
