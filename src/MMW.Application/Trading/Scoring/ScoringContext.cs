using MMW.Application.MarketData.Models;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring;

/// <summary>Kết quả chấm của MỘT tiêu chí.</summary>
/// <param name="Reason">Tiếng Việt, PHẢI nêu số liệu thực tế so với ngưỡng.</param>
/// <param name="DataAvailable">Sai ⟹ <paramref name="AwardedPoints"/> phải bằng 0 (FR-006).</param>
/// <param name="IsApproximation">Đúng khi con số là xấp xỉ, không phải đo được (R-010).</param>
public sealed record CriterionResult(
    int AwardedPoints,
    string Reason,
    bool DataAvailable = true,
    bool IsHardVeto = false,
    VetoReason? VetoReason = null,
    bool IsApproximation = false,
    string? StateCode = null)
{
    /// <summary>
    /// Thiếu dữ liệu ⟹ 0 điểm, không phải điểm trung bình và cũng không phải điểm tối đa (FR-006).
    /// </summary>
    /// <remarks>
    /// Có hàm dựng riêng để không ai phải nhớ đặt <c>AwardedPoints = 0</c> cùng lúc với
    /// <c>DataAvailable = false</c>. Quên một trong hai là cách âm thầm để một nguồn dữ liệu
    /// chết biến thành điểm thưởng.
    /// </remarks>
    public static CriterionResult Missing(string reason) => new(0, reason, DataAvailable: false);

    public static CriterionResult Veto(VetoReason reason, string detail) =>
        new(0, detail, IsHardVeto: true, VetoReason: reason);
}

/// <summary>Hợp đồng plug-in của một tiêu chí chấm điểm (Nguyên tắc V).</summary>
/// <remarks>
/// <c>Evaluate</c> PHẢI thuần: không mạng, không cơ sở dữ liệu, không đồng hồ. Mọi dữ liệu đã
/// nằm sẵn trong <see cref="ScoringContext"/>. Đây là thứ khiến kiểm thử lịch sử chạy nhanh —
/// nạp dữ liệu một lần rồi chấm hàng chục nghìn lần trong bộ nhớ.
/// </remarks>
public interface IScoreCriterion
{
    /// <summary>Định danh ổn định, ví dụ <c>technical.htf_alignment</c>. KHÔNG đổi sau khi có dữ liệu lịch sử.</summary>
    string Key { get; }

    ScoreGroup Group { get; }

    /// <summary>Điểm tối đa tiêu chí này đóng góp.</summary>
    int MaxPoints { get; }

    /// <summary>
    /// Kết quả của tiêu chí này có ĐỔI theo <see cref="ScoringContext.Direction"/> không.
    /// </summary>
    /// <remarks>
    /// Khai báo bắt buộc, không có giá trị mặc định — người viết tiêu chí mới phải trả lời câu
    /// hỏi này một cách có ý thức. Nó quyết định phần điểm nào được đem ra SO SÁNH giữa hai chiều
    /// ở §4: một tiêu chí cho cùng số điểm cho cả mua lẫn bán không nói gì về việc nên đi chiều
    /// nào, nên gộp nó vào phép so là làm loãng đúng phần cần so.
    ///
    /// Khai báo sai theo hướng "nói không nhưng thật ra có" là lỗi im lặng, nên có một test gác
    /// riêng chấm mọi tiêu chí ở cả hai chiều và bắt những tiêu chí trả kết quả khác nhau.
    /// </remarks>
    bool IsDirectional { get; }

    CriterionResult Evaluate(ScoringContext context);
}

/// <summary>
/// Thống kê hành vi của trader, tính sẵn trước khi chấm điểm.
/// </summary>
/// <remarks>
/// Nằm trong context thay vì được truy vấn bên trong tiêu chí, để ràng buộc "tiêu chí không
/// gọi I/O" không có ngoại lệ nào.
/// </remarks>
public sealed record TraderStatistics(
    int ConsecutiveLosses,
    decimal DailyLossPercent,
    DateTime? LastLossClosedAtUtc,
    decimal? AverageRiskRecent,
    int TradesToday,
    int ClosedTradeCount,
    IReadOnlyList<int> WorstHoursUtc)
{
    /// <summary>
    /// Chuỗi thua chỉ tính các lệnh đóng trong ngày UTC hiện tại. Dùng cho hành động
    /// StopForDay; <see cref="ConsecutiveLosses"/> xuyên ngày chỉ dùng để giảm kích thước.
    /// </summary>
    public int ConsecutiveLossesToday { get; init; }

    /// <summary>
    /// Các vị thế ĐANG MỞ tại thời điểm chấm điểm.
    /// </summary>
    /// <remarks>
    /// Tách hẳn khỏi <see cref="TradesToday"/>, và đó là điểm mấu chốt. <c>TradesToday</c> đếm
    /// số lệnh đã VÀO trong ngày — nó không nói gì về việc bao nhiêu lệnh còn đang chạy. Trước
    /// khi có trường này, không tầng nào của hệ thống biết mình đang mở vị thế nào, nên một
    /// setup tốt chấm ≥55 điểm suốt 3–5 nến liền sẽ được vào 3–5 lần: không phải ba lệnh độc
    /// lập, mà MỘT ý tưởng vào làm ba lần, cùng chiều, cùng mã, dừng lỗ nằm sát nhau. Chúng
    /// thắng cùng nhau và thua cùng nhau.
    /// </remarks>
    public IReadOnlyList<OpenPositionSnapshot> OpenPositions { get; init; } = Array.Empty<OpenPositionSnapshot>();

    /// <summary>Tài khoản chưa có lịch sử — dùng khi không tính được thống kê.</summary>
    public static TraderStatistics Empty { get; } =
        new(0, 0m, null, null, 0, 0, Array.Empty<int>());
}

/// <summary>Một vị thế đang mở, rút gọn về đúng những gì gate kỷ luật cần biết.</summary>
/// <param name="SizeR">Kích thước theo R tại lúc vào lệnh, để cộng dồn rủi ro tương quan.</param>
public sealed record OpenPositionSnapshot(
    string Symbol,
    TradeDirection Direction,
    decimal SizeR);

/// <summary>
/// Đầu vào BẤT BIẾN của một lần chấm điểm.
/// </summary>
/// <remarks>
/// Là <c>record</c> bất biến có chủ đích: một tiêu chí không thể vô tình làm bẩn đầu vào của
/// tiêu chí chạy sau nó, nên thứ tự duyệt không ảnh hưởng kết quả.
/// </remarks>
public sealed record ScoringContext
{
    public required string Symbol { get; init; }
    public required DateTime EvaluatedAtUtc { get; init; }
    public required DateTime CandleCloseTimeUtc { get; init; }
    public required TradeDirection Direction { get; init; }

    /// <summary>Nến 15m ĐÃ ĐÓNG, mới nhất ở cuối.</summary>
    public required IReadOnlyList<Candle> EntryCandles { get; init; }

    /// <summary>Nến 4h đã đóng.</summary>
    public required IReadOnlyList<Candle> BiasCandles { get; init; }

    /// <summary>Nến 1d đã đóng.</summary>
    public required IReadOnlyList<Candle> DailyCandles { get; init; }

    /// <summary>
    /// Nến khung NHANH (5m). Rỗng khi không lấy được — mọi thứ đọc nó phải chịu được điều đó.
    /// </summary>
    /// <remarks>
    /// Chỉ phục vụ nhánh vào-ngay-khi-MA-cắt: cú cắt trên khung 15m chỉ nhìn thấy được sau khi
    /// nến 15m đóng, tức chậm nhất 15 phút so với lúc nó thật sự xảy ra. Khung 5m rút độ trễ đó
    /// xuống 5 phút, và đó là toàn bộ lý do nó có mặt ở đây.
    ///
    /// KHÔNG dùng cho chấm điểm hay dựng mức: thang điểm và mức cấu trúc neo vào khung vào lệnh,
    /// trộn thêm một khung nữa vào đó sẽ làm hai phiếu cùng điểm nói về hai thứ khác nhau.
    /// </remarks>
    public IReadOnlyList<Candle> FastCandles { get; init; } = Array.Empty<Candle>();

    /// <summary>Giá hiện tại từ ticker, KHÔNG lấy từ nến đang chạy.</summary>
    public required decimal CurrentPrice { get; init; }

    public required DailyPlan DailyPlan { get; init; }
    public required EngineSetting Settings { get; init; }
    public required TraderStatistics TraderStats { get; init; }
    public required IReadOnlyList<MarketContextRecord> ActiveAiContext { get; init; }

    // ── Có thể null khi nguồn không khả dụng → tiêu chí trả DataAvailable = false ──
    public FundingSnapshot? Funding { get; init; }
    public OpenInterestSeries? OpenInterest { get; init; }
    public DepthSnapshot? Depth { get; init; }
    public LongShortRatio? LongShort { get; init; }

    /// <summary>Hệ số tương quan với mã dẫn dắt. Null với chính mã dẫn dắt hoặc khi thiếu dữ liệu.</summary>
    public decimal? LeaderCorrelation { get; init; }

    /// <summary>Điểm chất lượng khung giờ, tính sẵn ở ngoài để tiêu chí không phải gọi I/O.</summary>
    public SessionQuality? SessionQuality { get; init; }

    /// <summary>
    /// Hợp lưu price action, quét MỘT LẦN cho cả lượt chấm.
    /// </summary>
    /// <remarks>
    /// Trước V2, ba tiêu chí kỹ thuật mỗi cái tự dựng một <c>PriceActionAnalyzer</c> rồi gọi
    /// <c>Analyze</c> với ĐÚNG cùng bộ tham số. Mỗi lần gọi phát hiện lại điểm xoay và tính lại
    /// RSI hai lần; trên 70.000 mốc kiểm thử × 2 mã đó là hàng trăm nghìn lượt chạy thừa hai phần
    /// ba. Nó cũng đi ngược điều đã tuyên bố ngay trên <see cref="IScoreCriterion"/>: mọi dữ liệu
    /// đã nằm sẵn trong bối cảnh, tiêu chí chỉ đọc.
    ///
    /// Bản ghi KHÔNG phụ thuộc chiều lệnh, nên khi §4 chấm cả hai chiều thì một lần quét dùng
    /// được cho cả hai — chấm hai chiều rẻ hơn một lần chấm của phiên bản cũ.
    /// </remarks>
    public PriceActionSignals PriceAction { get; init; } = PriceActionSignals.None;

    /// <summary>Mức dừng lỗ dự kiến, suy ra từ cấu trúc giá trước khi chấm điểm.</summary>
    public decimal? PlannedStopLoss { get; init; }

    /// <summary>Mức chốt lời dự kiến.</summary>
    public decimal? PlannedTakeProfit { get; init; }

    /// <summary>Cản gần để chốt phần đầu; có thể gần hơn mục tiêu cuối dùng cho rào R:R.</summary>
    public decimal? PlannedFirstTakeProfit { get; init; }

    /// <summary>Mục tiêu xa để giữ runner; null khi TP1 cũng là mục tiêu cuối.</summary>
    public decimal? PlannedRunnerTakeProfit { get; init; }

    /// <summary>Giá limit retest theo pivot/EMA hoặc biên range.</summary>
    public decimal? PlannedLimitEntry { get; init; }

    /// <summary>
    /// Kết quả dựng mức theo cấu trúc. Null khi cấu trúc nằm quá xa để đặt được dừng lỗ.
    /// </summary>
    /// <remarks>
    /// Tính sẵn ở ngoài như mọi đầu vào khác, để tiêu chí giữ được ràng buộc "không I/O, không
    /// dựng lại chỉ báo". Null ở đây là một KẾT LUẬN — "không có chỗ đặt dừng lỗ hợp lệ" — chứ
    /// không phải thiếu dữ liệu, và <c>technical.structural_room</c> biến nó thành veto cứng.
    /// </remarks>
    public Structure.StructuralLevels? StructuralLevels { get; init; }

    /// <summary>Mã dẫn dắt của thị trường. Mọi tương quan đều đo so với mã này.</summary>
    public const string LeaderSymbol = "BTCUSDT";

    /// <summary>Mã này có phải mã dẫn dắt của thị trường không (BTC).</summary>
    public bool IsLeaderSymbol => string.Equals(Symbol, LeaderSymbol, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Kết quả tổng hợp của cả vòng chấm điểm.</summary>
/// <param name="TotalMaxPoints">
/// Tổng điểm tối đa của mọi tiêu chí cộng điểm (85 với bộ hiện tại). Tính từ chính các tiêu chí
/// chứ không viết cứng, để thêm/bớt tiêu chí không âm thầm làm sai mọi phép so ngưỡng.
/// </param>
/// <param name="AvailableMaxPoints">
/// Phần của <paramref name="TotalMaxPoints"/> thực sự ĐO ĐƯỢC lần này.
/// </param>
/// <param name="DirectionalScore">
/// Phần điểm đến từ các tiêu chí ĐỔI THEO CHIỀU lệnh — con số dùng để so hai chiều ở §4.
/// </param>
/// <param name="DirectionalMaxPoints">Thang điểm của riêng nhóm đó, để đọc được tỉ lệ.</param>
/// <remarks>
/// Hai trường cuối tồn tại để sửa một lệch nguy hiểm giữa hai môi trường. Kiểm thử lịch sử KHÔNG
/// dựng lại được <c>liquidity.open_interest</c> và <c>liquidity.spread_depth</c> — 10 điểm luôn
/// bằng 0 — nên trần thực tế của nó là 75 chứ không phải 85. Ngưỡng vào lệnh 55 vì vậy đòi hỏi
/// 73,3% ở kiểm thử nhưng chỉ 64,7% ở chạy thật: <b>kiểm thử lọc gắt hơn chạy thật gần 9 điểm
/// phần trăm</b>.
///
/// Đó là loại lệch tệ nhất — nó làm kết quả kiểm thử đẹp hơn thực tế, theo một chiều, trong im
/// lặng. Chạy thật sẽ nhận thêm cả một nhóm lệnh điểm 55–62 mà kiểm thử chưa bao giờ nhìn thấy.
/// </remarks>
public sealed record ScoringOutcome(
    int TotalScore,
    int TechnicalScore,
    int MarketScore,
    int LiquidityScore,
    int DisciplinePenalty,
    bool IsVetoed,
    VetoReason? VetoReason,
    string? VetoDetail,
    IReadOnlyList<ScoredLine> Lines,
    int TotalMaxPoints = 85,
    int AvailableMaxPoints = 85,
    int DirectionalScore = 0,
    int DirectionalMaxPoints = 0)
{
    /// <summary>
    /// Tỉ lệ dữ liệu đo được lần này, trong <c>[0, 1]</c>. Dùng làm hệ số co kích thước.
    /// </summary>
    /// <remarks>
    /// Chuẩn hoá ngưỡng bỏ đi hình phạt "khó vào lệnh hơn" của FR-006, nên hình phạt phải quay
    /// lại ở chỗ khác — và chỗ đúng là kích thước. Cùng một setup, càng mù thì vào càng nhỏ.
    /// </remarks>
    public decimal DataCoverage => TotalMaxPoints <= 0
        ? 0m
        : Math.Clamp((decimal)AvailableMaxPoints / TotalMaxPoints, 0m, 1m);

    /// <summary>
    /// Điểm đã đạt có với tới <paramref name="threshold"/> trên thang <see cref="TotalMaxPoints"/> không.
    /// </summary>
    /// <remarks>
    /// So bằng phép NHÂN CHÉO thay vì chia: chia số nguyên sẽ cắt cụt, còn chia thập phân đưa
    /// sai số dấu phẩy động vào đúng chỗ quyết định vào lệnh hay không.
    /// </remarks>
    public bool Reaches(int threshold) =>
        AvailableMaxPoints > 0
        && (long)TotalScore * TotalMaxPoints >= (long)threshold * AvailableMaxPoints;
}

/// <summary>Một dòng phiếu: tiêu chí nào, bao nhiêu điểm, vì sao.</summary>
public sealed record ScoredLine(
    string Key,
    ScoreGroup Group,
    int MaxPoints,
    CriterionResult Result);

/// <summary>
/// Tổng hợp kết quả các gate kỷ luật. Đầy đủ ở US4; ở đây là giá trị trung tính.
/// </summary>
/// <remarks>
/// <see cref="SizeMultiplier"/> PHẢI ≤ 1.0 — không gate nào được làm lệnh to lên.
/// </remarks>
public sealed record GateAggregate(
    decimal SizeMultiplier,
    int ScorePenalty,
    bool IsBlocked,
    VetoReason? VetoReason,
    string? Detail)
{
    /// <summary>Không gate nào can thiệp.</summary>
    public static GateAggregate Neutral { get; } = new(1.0m, 0, false, null, null);
}
