using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring;

/// <summary>
/// Hợp lưu price action đo được tại một thời điểm, kèm TUỔI của từng tín hiệu.
/// </summary>
/// <remarks>
/// <para><b>Tuổi thay cho bật/tắt.</b> Trước V2 mỗi tín hiệu là một <c>bool</c> và không có khái
/// niệm "mẫu hình này hoàn thành cách đây bao lâu". Một cú hai đáy phá neckline lúc 09:00 vẫn trả
/// <c>true</c> lúc 17:00, nên bộ chấm điểm cho 8/10 điểm cấu trúc cho một setup đã cũ tám tiếng —
/// đúng loại lệnh vào muộn mà <c>technical.entry_location</c> sinh ra để chặn. Nay mỗi tín hiệu
/// mang số nến kể từ lúc nó hoàn thành, và trọng số hợp lưu giảm TUYẾN TÍNH theo tuổi thay vì
/// rơi từ 1 xuống 0 ở một mốc tuỳ ý.</para>
///
/// <para><b>Không phụ thuộc chiều lệnh.</b> Cả hai nhánh Fibonacci được tính sẵn, nên một bản ghi
/// dùng được cho CẢ hai chiều. Đó là điều kiện để §4 chấm hai chiều mà không phải phát hiện lại
/// điểm xoay và tính lại RSI thêm một lượt.</para>
/// </remarks>
/// <param name="FibonacciLong">Không có tuổi: đây là câu hỏi "giá HIỆN đang nằm trong vùng hồi không".</param>
public sealed record PriceActionSignals(
    int? BullishStaircase,
    int? BearishStaircase,
    int? DoubleBottom,
    int? DoubleTop,
    int? InverseHeadAndShoulders,
    int? HeadAndShoulders,
    int? BullishRsiDivergence,
    int? BearishRsiDivergence,
    bool FibonacciLong,
    bool FibonacciShort)
{
    /// <summary>Không tín hiệu nào — dùng khi thiếu nến hoặc không tính được biên độ dao động.</summary>
    public static PriceActionSignals None { get; } =
        new(null, null, null, null, null, null, null, null, false, false);

    public bool FibonacciConfluence(TradeDirection direction) =>
        direction == TradeDirection.Long ? FibonacciLong : FibonacciShort;

    /// <summary>
    /// Trọng số của một tín hiệu theo tuổi: <c>1 − age / maxAge</c>, kẹp về <c>[0, 1]</c>.
    /// </summary>
    /// <remarks>
    /// Tuyến tính chứ không bậc thang, vì một mẫu hình không đột ngột hết giá trị ở nến thứ 13.
    /// Tại <c>age == maxAge</c> trọng số bằng 0, nên "hết hạn" và "vừa đủ hạn" trùng nhau — không
    /// có khe hở nào giữa hai định nghĩa.
    /// </remarks>
    public static decimal Weight(int? ageBars, int maxAgeBars)
    {
        if (ageBars is not { } age || age < 0 || maxAgeBars <= 0 || age >= maxAgeBars) return 0m;
        return 1m - (decimal)age / maxAgeBars;
    }

    /// <summary>Tổng trọng số các mẫu hình THUẬN chiều còn hiệu lực.</summary>
    public decimal SupportWeight(TradeDirection direction, int maxAgeBars) =>
        direction == TradeDirection.Long
            ? Sum(maxAgeBars, BullishStaircase, DoubleBottom, InverseHeadAndShoulders, BullishRsiDivergence)
            : Sum(maxAgeBars, BearishStaircase, DoubleTop, HeadAndShoulders, BearishRsiDivergence);

    /// <summary>Tổng trọng số các mẫu hình NGƯỢC chiều còn hiệu lực.</summary>
    public decimal OpposeWeight(TradeDirection direction, int maxAgeBars) =>
        SupportWeight(Opposite(direction), maxAgeBars);

    public bool Supports(TradeDirection direction, int maxAgeBars) => SupportWeight(direction, maxAgeBars) > 0m;

    public bool Opposes(TradeDirection direction, int maxAgeBars) => OpposeWeight(direction, maxAgeBars) > 0m;

    /// <summary>
    /// Hợp lưu RÒNG: trọng số thuận trừ trọng số ngược.
    /// </summary>
    /// <remarks>
    /// Đây là thứ mọi tiêu chí phải dùng, không phải <see cref="Supports"/>. Hai đáy và phân kỳ
    /// RSI giảm có thể cùng đúng một lúc; hỏi "có mẫu hình thuận nào không" sẽ trả về CÓ và vứt
    /// bằng chứng ngược đi trong im lặng. Bằng chứng mâu thuẫn phải LÀM GIẢM độ tin cậy — một
    /// thị trường đang nói hai điều trái nhau là một thị trường chưa quyết định.
    /// </remarks>
    public decimal NetConfluence(TradeDirection direction, int maxAgeBars) =>
        SupportWeight(direction, maxAgeBars) - OpposeWeight(direction, maxAgeBars);

    /// <summary>Tên các mẫu hình thuận chiều còn hiệu lực, để ghi vào phiếu.</summary>
    public IReadOnlyList<string> SupportingNames(TradeDirection direction, int maxAgeBars)
    {
        var names = new List<string>(4);

        void Add(int? age, string name)
        {
            if (Weight(age, maxAgeBars) <= 0m) return;
            names.Add(age is 0 ? name : $"{name} ({age} nến trước)");
        }

        if (direction == TradeDirection.Long)
        {
            Add(BullishStaircase, "bậc thang tăng");
            Add(DoubleBottom, "hai đáy");
            Add(InverseHeadAndShoulders, "vai-đầu-vai ngược");
            Add(BullishRsiDivergence, "RSI phân kỳ tăng");
        }
        else
        {
            Add(BearishStaircase, "bậc thang giảm");
            Add(DoubleTop, "hai đỉnh");
            Add(HeadAndShoulders, "vai-đầu-vai");
            Add(BearishRsiDivergence, "RSI phân kỳ giảm");
        }

        return names;
    }

    private static TradeDirection Opposite(TradeDirection d) =>
        d == TradeDirection.Long ? TradeDirection.Short : TradeDirection.Long;

    private static decimal Sum(int maxAgeBars, params int?[] ages)
    {
        var total = 0m;
        foreach (var age in ages) total += Weight(age, maxAgeBars);
        return total;
    }
}

/// <summary>
/// Nhận diện hợp lưu price action bằng pivot đã xác nhận. Mọi ngưỡng dùng ATR để không phụ
/// thuộc đơn vị giá; không mẫu hình nào tự có quyền phát lệnh.
/// </summary>
/// <remarks>
/// Các hằng số hình học dưới đây là ĐỊNH NGHĨA của từng mẫu hình, không phải khẩu vị rủi ro —
/// đổi chúng nghĩa là nhận diện một hình khác, chứ không phải chỉnh một ngưỡng. Vì vậy chúng
/// nằm trong mã kèm lý do, còn <c>PatternMaxAgeBars</c> (mẫu hình cũ bao lâu thì hết hiệu lực)
/// nằm ở <c>EngineSetting</c>: đó mới là khẩu vị.
/// </remarks>
public sealed class PriceActionAnalyzer
{
    private const int AnalysisWindow = 100;
    private const int RsiPeriod = 14;

    /// <summary>
    /// Chu kỳ biên độ dao động. TÁCH khỏi <see cref="RsiPeriod"/> dù hiện cùng bằng 14.
    /// </summary>
    /// <remarks>
    /// Trước đây ATR được tính bằng chính hằng số RSI. Hai con số trùng nhau nên vô hại, nhưng
    /// đổi chu kỳ RSI sẽ âm thầm đổi luôn mọi ngưỡng tính theo ATR trong lớp này — và không có
    /// test nào đỏ vì cả hai vẫn "chạy đúng", chỉ là đo bằng một cái thước khác.
    /// </remarks>
    private const int AtrPeriod = 14;

    // ── Hai đáy / hai đỉnh ──────────────────────────────────────────────

    private const decimal EqualPivotToleranceAtr = 0.4m;

    /// <summary>Khoảng cách tối thiểu giữa hai đáy (2 giờ trên khung 15m).</summary>
    /// <remarks>
    /// Hai đáy cách nhau ba nến trên một vùng đi ngang phẳng thoả mọi điều kiện về giá. Đó không
    /// phải hai đáy, đó là một cái nền — và một cái nền không có bên bán bị kiệt sức để tận dụng.
    /// </remarks>
    private const int MinPivotGapBars = 8;

    /// <summary>Cú nảy giữa hai đáy phải cao hơn đáy CAO NHẤT ít nhất bấy nhiêu biên độ.</summary>
    /// <remarks>
    /// Không có cú nảy thật thì không có hai đáy — chỉ có một vùng tích luỹ. Ràng buộc này và
    /// <see cref="MinPivotGapBars"/> chặn hai kiểu giả khác nhau: một cái phẳng theo giá, một cái
    /// ngắn theo thời gian.
    /// </remarks>
    private const decimal MinReboundAtr = 1.0m;

    // ── Vai-đầu-vai ─────────────────────────────────────────────────────

    /// <summary>Hai vai được phép lệch nhau tối đa bấy nhiêu biên độ.</summary>
    private const decimal ShoulderToleranceAtr = 0.5m;

    /// <summary>Đầu phải nhô hơn vai cao nhất ít nhất bấy nhiêu biên độ.</summary>
    /// <remarks>
    /// V1 để 0,25 trong khi dung sai vai là 0,60 — hai vai được phép lệch nhau GẤP 2,4 LẦN mức
    /// mà cái đầu phải nhô lên. Nghĩa là một đường zigzag bất kỳ với ba điểm xoay đều thoả, và
    /// đó là nguồn dương tính giả lớn nhất của lớp này.
    /// </remarks>
    private const decimal HeadProminenceAtr = 0.8m;

    /// <summary>Khoảng cách tối thiểu giữa vai và đầu. Ba điểm xoay dính nhau không phải mẫu hình.</summary>
    private const int MinShoulderGapBars = 6;

    /// <summary>Độ nghiêng tối đa của neckline. Neckline dốc nghĩa là giá đang trend, không phải đảo chiều.</summary>
    private const decimal MaxNecklineSlopeAtr = 0.5m;

    // ── Phân kỳ RSI ─────────────────────────────────────────────────────

    /// <summary>
    /// Chênh lệch RSI tối thiểu để gọi là phân kỳ.
    /// </summary>
    /// <remarks>
    /// V1 để 2 điểm RSI — nằm trong sai số làm tròn của chính chỉ báo, nên "phân kỳ" bật lên vì
    /// nhiễu chứ không vì động lượng suy yếu.
    /// </remarks>
    private const decimal MinRsiDivergence = 5m;

    private const int MinDivergenceGapBars = 5;
    private const int MaxDivergenceGapBars = 50;

    /// <summary>Điểm xoay ĐẦU phải nằm trong vùng cực trị, nếu không thì phân kỳ không nói gì.</summary>
    /// <remarks>
    /// Phân kỳ từ RSI 50 chỉ là hai con số khác nhau. Phân kỳ từ RSI 28 mới là bên bán đã kiệt
    /// sức — và đó là toàn bộ ý nghĩa mà tín hiệu này định mang.
    /// </remarks>
    private const decimal OversoldPivotRsi = 35m;

    private const decimal OverboughtPivotRsi = 65m;

    /// <summary>
    /// Điểm xoay phải nằm sau ít nhất bấy nhiêu nến trong cửa sổ thì RSI tại đó mới đáng tin.
    /// </summary>
    /// <remarks>
    /// RSI được tính lại trên <c>window.Take(index + 1)</c>. Làm trơn Wilder là một hồi quy khởi
    /// tạo ở đầu chuỗi, nên với <c>index</c> nhỏ giá trị lệch đáng kể so với RSI cuộn thật. Ba
    /// chu kỳ là mốc quy ước để coi phần khởi tạo đã tan hết.
    /// </remarks>
    private const int MinRsiPivotIndex = 3 * RsiPeriod;

    // ── Fibonacci ───────────────────────────────────────────────────────

    /// <summary>Nhịp đẩy nhỏ hơn mức này không sinh ra vùng hồi đáng tin.</summary>
    /// <remarks>
    /// Không có sàn thì một nhịp 0,3 ATR sinh ra "vùng hồi" rộng 0,04 ATR — giá chạm vào đó là
    /// chuyện ngẫu nhiên, không phải một mức.
    /// </remarks>
    private const decimal MinImpulseAtr = 1.5m;

    /// <summary>
    /// Vùng cộng điểm thu về đúng "golden pocket" 0,5–0,618.
    /// </summary>
    /// <remarks>
    /// Dải 0,382–0,786 rộng tới mức gần như mọi nhịp hồi đều rơi vào, nên nó không phân biệt được
    /// gì. Hai vùng ngoài rìa vẫn là nhịp hồi hợp lệ, chỉ là không được cộng điểm.
    /// </remarks>
    private const decimal GoldenPocketLower = 0.5m;

    private const decimal GoldenPocketUpper = 0.618m;

    private readonly ISwingDetector _swings;
    private readonly IIndicatorService _indicators;

    public PriceActionAnalyzer(ISwingDetector swings, IIndicatorService indicators)
    {
        _swings = swings;
        _indicators = indicators;
    }

    /// <summary>
    /// Quét toàn bộ mẫu hình trên cửa sổ nến. Kết quả KHÔNG phụ thuộc chiều lệnh.
    /// </summary>
    /// <param name="currentPrice">
    /// Chỉ dùng để ĐO KHOẢNG CÁCH (Fibonacci). Không mẫu hình nào được hoàn thành bằng giá này:
    /// ở chạy thật nó là giá ticker chạy trong nến, ở kiểm thử lịch sử nó là giá đóng nến — cùng
    /// một chuỗi nến sẽ cho hai kết quả khác nhau ở hai môi trường, và ở chạy thật mẫu hình nhấp
    /// nháy bật/tắt suốt 15 phút.
    /// </param>
    public PriceActionSignals Analyze(IReadOnlyList<Candle> candles, int pivotBars, decimal currentPrice)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (candles.Count == 0) return PriceActionSignals.None;

        var window = candles.TakeLast(AnalysisWindow).ToList();
        var atr = _indicators.Atr(window, AtrPeriod);
        if (atr is null or <= 0m) return PriceActionSignals.None;

        var last = window.Count - 1;
        var pivots = _swings.Detect(window, Math.Max(1, pivotBars));
        var highs = pivots.Where(p => p.IsHigh).ToList();
        var lows = pivots.Where(p => !p.IsHigh).ToList();

        return new PriceActionSignals(
            BullishStaircase: Staircase(highs, lows, last, ascending: true),
            BearishStaircase: Staircase(highs, lows, last, ascending: false),
            DoubleBottom: DoublePattern(window, lows, atr.Value, bullish: true, last),
            DoubleTop: DoublePattern(window, highs, atr.Value, bullish: false, last),
            InverseHeadAndShoulders: ThreePivotPattern(window, lows, atr.Value, bullish: true, last),
            HeadAndShoulders: ThreePivotPattern(window, highs, atr.Value, bullish: false, last),
            BullishRsiDivergence: RsiDivergence(window, lows, bullish: true, last),
            BearishRsiDivergence: RsiDivergence(window, highs, bullish: false, last),
            FibonacciLong: InFibonacciRetracement(pivots, TradeDirection.Long, currentPrice, atr.Value),
            FibonacciShort: InFibonacciRetracement(pivots, TradeDirection.Short, currentPrice, atr.Value));
    }

    // ── Bậc thang ───────────────────────────────────────────────────────

    /// <summary>Tuổi tính từ nến XÁC NHẬN điểm xoay muộn nhất trong sáu điểm.</summary>
    private static int? Staircase(
        IReadOnlyList<SwingPoint> highs, IReadOnlyList<SwingPoint> lows, int lastIndex, bool ascending)
    {
        if (highs.Count < 3 || lows.Count < 3) return null;

        var h = highs.TakeLast(3).ToList();
        var l = lows.TakeLast(3).ToList();
        if (!Monotonic(h, ascending) || !Monotonic(l, ascending)) return null;

        var confirmed = h.Concat(l).Max(p => p.ConfirmedAtIndex);
        return Math.Max(0, lastIndex - confirmed);
    }

    private static bool Monotonic(IReadOnlyList<SwingPoint> points, bool ascending)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (ascending ? points[i].Price <= points[i - 1].Price : points[i].Price >= points[i - 1].Price)
                return false;
        }
        return true;
    }

    // ── Hai đáy / hai đỉnh ──────────────────────────────────────────────

    private static int? DoublePattern(
        IReadOnlyList<Candle> window, IReadOnlyList<SwingPoint> points, decimal atr, bool bullish, int lastIndex)
    {
        if (points.Count < 2) return null;

        var first = points[^2];
        var second = points[^1];

        if (Math.Abs(first.Price - second.Price) > atr * EqualPivotToleranceAtr) return null;
        if (second.Index - first.Index < MinPivotGapBars) return null;

        var between = Between(window, first.Index, second.Index);
        if (between.Count == 0) return null;

        var rebound = bullish
            ? between.Max(c => c.High) - Math.Max(first.Price, second.Price)
            : Math.Min(first.Price, second.Price) - between.Min(c => c.Low);
        if (rebound < atr * MinReboundAtr) return null;

        // Neckline của hai đáy là đỉnh nằm GIỮA CHÚNG, không phải đỉnh cao nhất giữa mọi cặp đáy
        // trong cửa sổ 100 nến.
        var neckline = bullish ? between.Max(c => c.High) : between.Min(c => c.Low);
        return BreakoutAge(window, neckline, bullish, notBefore: second.Index, lastIndex);
    }

    // ── Vai-đầu-vai ─────────────────────────────────────────────────────

    private static int? ThreePivotPattern(
        IReadOnlyList<Candle> window, IReadOnlyList<SwingPoint> points, decimal atr, bool bullish, int lastIndex)
    {
        if (points.Count < 3) return null;

        var p = points.TakeLast(3).ToArray();

        if (p[1].Index - p[0].Index < MinShoulderGapBars) return null;
        if (p[2].Index - p[1].Index < MinShoulderGapBars) return null;

        var shoulderGap = Math.Abs(p[0].Price - p[2].Price);
        if (shoulderGap > atr * ShoulderToleranceAtr) return null;

        var prominence = bullish
            ? Math.Min(p[0].Price, p[2].Price) - p[1].Price
            : p[1].Price - Math.Max(p[0].Price, p[2].Price);
        if (prominence < atr * HeadProminenceAtr) return null;

        // Neckline thật nối CẢ HAI điểm trung gian. Chỉ đọc đoạn đầu→vai-phải thường cho mức dễ
        // phá hơn ⟹ xác nhận sớm hơn thực tế — đúng loại sai lệch một chiều làm đẹp kết quả.
        var left = SegmentLevel(window, p[0].Index, p[1].Index, bullish);
        var right = SegmentLevel(window, p[1].Index, p[2].Index, bullish);
        if (left is null || right is null) return null;

        if (Math.Abs(left.Value - right.Value) > atr * MaxNecklineSlopeAtr) return null;

        var neckline = bullish
            ? Math.Max(left.Value, right.Value)
            : Math.Min(left.Value, right.Value);

        return BreakoutAge(window, neckline, bullish, notBefore: p[2].Index, lastIndex);
    }

    // ── Phá neckline ────────────────────────────────────────────────────

    /// <summary>
    /// Số nến kể từ khi giá ĐÓNG vượt hẳn neckline và ở nguyên bên đó cho tới hiện tại.
    /// </summary>
    /// <remarks>
    /// Lấy đầu của DẢI LIÊN TỤC đang có hiệu lực chứ không phải lần phá đầu tiên trong cửa sổ.
    /// Một cú phá thất bại rồi phá lại là một mẫu hình mới, không phải mẫu hình cũ già đi; đo từ
    /// lần phá đầu sẽ khai tử đúng những tín hiệu vừa mới hình thành.
    ///
    /// Giá đóng cửa hiện tại phải CÒN ở bên kia neckline — phá rồi tụt lại là cú phá hỏng, và một
    /// cú phá hỏng không phải bằng chứng thuận chiều với tuổi còn trẻ.
    /// </remarks>
    private static int? BreakoutAge(
        IReadOnlyList<Candle> window, decimal neckline, bool bullish, int notBefore, int lastIndex)
    {
        bool Beyond(Candle c) => bullish ? c.Close > neckline : c.Close < neckline;

        if (!Beyond(window[lastIndex])) return null;

        var start = lastIndex;
        while (start - 1 >= 0 && Beyond(window[start - 1])) start--;

        // Cú phá phải xảy ra SAU điểm xoay cuối cùng của mẫu hình. Một cây nến đóng bên kia
        // neckline từ trước khi mẫu hình tồn tại không phải cú phá của mẫu hình đó.
        if (start < notBefore) return null;

        return lastIndex - start;
    }

    /// <summary>Mức khó phá nhất của đoạn nằm GIỮA hai điểm xoay. Null khi hai điểm dính nhau.</summary>
    private static decimal? SegmentLevel(IReadOnlyList<Candle> window, int from, int to, bool bullish)
    {
        var between = Between(window, from, to);
        if (between.Count == 0) return null;
        return bullish ? between.Max(c => c.High) : between.Min(c => c.Low);
    }

    private static List<Candle> Between(IReadOnlyList<Candle> window, int from, int to)
    {
        if (to <= from + 1) return new List<Candle>();
        return window.Skip(from + 1).Take(to - from - 1).ToList();
    }

    // ── Phân kỳ RSI ─────────────────────────────────────────────────────

    private int? RsiDivergence(
        IReadOnlyList<Candle> window, IReadOnlyList<SwingPoint> points, bool bullish, int lastIndex)
    {
        if (points.Count < 2) return null;

        var a = points[^2];
        var b = points[^1];

        var gap = b.Index - a.Index;
        if (gap < MinDivergenceGapBars || gap > MaxDivergenceGapBars) return null;
        if (a.Index < MinRsiPivotIndex) return null;

        var rsiA = _indicators.Rsi(window.Take(a.Index + 1).Select(c => c.Close).ToList(), RsiPeriod);
        var rsiB = _indicators.Rsi(window.Take(b.Index + 1).Select(c => c.Close).ToList(), RsiPeriod);
        if (rsiA is null || rsiB is null) return null;

        if (bullish ? rsiA > OversoldPivotRsi : rsiA < OverboughtPivotRsi) return null;

        var diverged = bullish
            ? b.Price < a.Price && rsiB >= rsiA + MinRsiDivergence
            : b.Price > a.Price && rsiB <= rsiA - MinRsiDivergence;

        return diverged ? Math.Max(0, lastIndex - b.ConfirmedAtIndex) : null;
    }

    // ── Fibonacci ───────────────────────────────────────────────────────

    private static bool InFibonacciRetracement(
        IReadOnlyList<SwingPoint> pivots, TradeDirection direction, decimal currentPrice, decimal atr)
    {
        if (pivots.Count < 2) return false;

        for (var i = pivots.Count - 1; i > 0; i--)
        {
            var end = pivots[i];
            var start = pivots.Take(i).LastOrDefault(p => p.IsHigh != end.IsHigh);
            if (start is null) continue;

            // `continue`, KHÔNG `return`. Một nhịp quá nhỏ chỉ nói rằng nhịp ĐÓ không dùng được,
            // không nói gì về các nhịp cũ hơn. Thoát sớm ở đây huỷ toàn bộ phần quét còn lại và
            // biến Fibonacci thành tín hiệu gần như không bao giờ bật trên vùng đi ngang.
            var impulse = Math.Abs(end.Price - start.Price);
            if (impulse < atr * MinImpulseAtr) continue;

            if (direction == TradeDirection.Long && !start.IsHigh && end.IsHigh)
            {
                var retracement = (end.Price - currentPrice) / impulse;
                return retracement is >= GoldenPocketLower and <= GoldenPocketUpper;
            }

            if (direction == TradeDirection.Short && start.IsHigh && !end.IsHigh)
            {
                var retracement = (currentPrice - end.Price) / impulse;
                return retracement is >= GoldenPocketLower and <= GoldenPocketUpper;
            }
        }

        return false;
    }
}
