using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;

namespace MMW.Application.Trading.Structure;

/// <summary>Xu hướng đọc được từ chuỗi điểm xoay khung lớn.</summary>
public enum HtfTrend
{
    /// <summary>Không có chuỗi đỉnh/đáy nhất quán — không phải "đi ngang", mà là "chưa đọc được".</summary>
    Unclear = 0,
    Up = 1,
    Down = 2,
}

/// <summary>Mức xác nhận của xu hướng khung lớn.</summary>
public enum HtfTrendStrength
{
    None = 0,

    /// <summary>Chuỗi đỉnh/đáy đã nhất quán, nhưng chồng EMA chưa xếp theo.</summary>
    Structural = 1,

    /// <summary>Chuỗi đỉnh/đáy nhất quán VÀ chồng EMA 20/50/200 xếp cùng chiều.</summary>
    Aligned = 2,
}

/// <summary>Loại mức đã sinh ra một lớp của vùng giá trị.</summary>
public enum HtfZoneLayer
{
    /// <summary>Đáy/đỉnh xoay 4h gần nhất cùng phía với nhịp hồi.</summary>
    SwingLevel = 1,

    /// <summary>Mức đã bị phá và đổi vai: kháng cự cũ thành hỗ trợ mới, hoặc ngược lại.</summary>
    FlippedLevel = 2,

    Ema20 = 3,
    Ema50 = 4,

    /// <summary>Dải Fibonacci 38,2–61,8% của chân đẩy 4h gần nhất.</summary>
    Fibonacci = 5,

    /// <summary>Thân nến 4h đã tạo ra cú phá cấu trúc — nơi lệnh chờ chưa khớp còn nằm lại.</summary>
    BreakoutBase = 6,
}

/// <summary>
/// Một vùng giá được nhiều mức khung lớn cùng chỉ vào.
/// </summary>
/// <remarks>
/// Vùng chứ không phải một mức, vì không mức nào trong số này chính xác tới từng đơn vị giá:
/// EMA dịch mỗi nến, đáy xoay là một điểm nhưng người ta đặt lệnh quanh nó, còn dải Fibonacci
/// vốn đã là một khoảng. Ép tất cả về một con số rồi chờ giá chạm đúng con số đó là cách chắc
/// chắn nhất để không bao giờ khớp lệnh nào.
/// </remarks>
public sealed record HtfValueZone(decimal Low, decimal High, IReadOnlyList<HtfZoneLayer> Layers)
{
    public decimal Mid => (Low + High) / 2m;

    /// <summary>Số lớp khác loại cùng chỉ vào vùng này.</summary>
    public int Confluence => Layers.Count;

    public bool Contains(decimal price) => price >= Low && price <= High;

    public string DescribeVi() => string.Join(" + ", Layers.Select(LayerNameVi));

    public static string LayerNameVi(HtfZoneLayer layer) => layer switch
    {
        HtfZoneLayer.SwingLevel => "đáy/đỉnh xoay 4h",
        HtfZoneLayer.FlippedLevel => "mức đã đổi vai",
        HtfZoneLayer.Ema20 => "EMA20 4h",
        HtfZoneLayer.Ema50 => "EMA50 4h",
        HtfZoneLayer.Fibonacci => "Fib 38–62%",
        HtfZoneLayer.BreakoutBase => "nền nến phá vỡ",
        _ => layer.ToString(),
    };
}

/// <summary>Kết quả đọc cấu trúc khung 4h tại một thời điểm.</summary>
/// <param name="InvalidationPrice">
/// Mức mà nếu giá đóng qua thì xu hướng coi như hỏng — đáy cao hơn gần nhất với xu hướng tăng.
/// Đây là ranh giới TUYỆT ĐỐI của dừng lỗ: đặt dừng lỗ ra ngoài nó là trả tiền để giữ một
/// nhận định đã sai.
/// </param>
/// <param name="BarsSinceConfirmed">Số nến 4h kể từ lúc điểm xoay cuối cùng được xác nhận.</param>
public sealed record HtfTrendRead(
    HtfTrend Trend,
    HtfTrendStrength Strength,
    decimal? LastSwingHigh,
    decimal? LastSwingLow,
    decimal? PriorSwingHigh,
    decimal? PriorSwingLow,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Ema200,
    decimal? Atr,
    decimal? InvalidationPrice,
    int BarsSinceConfirmed,
    string DetailVi)
{
    public static HtfTrendRead Unclear(string detail) =>
        new(HtfTrend.Unclear, HtfTrendStrength.None, null, null, null, null,
            null, null, null, null, null, 0, detail);

    /// <summary>Xu hướng này có thuận với chiều lệnh đang xét không.</summary>
    public bool Supports(Domain.Enums.TradeDirection direction) =>
        direction == Domain.Enums.TradeDirection.Long
            ? Trend == HtfTrend.Up
            : Trend == HtfTrend.Down;
}

public interface IHtfSwingAnalyzer
{
    /// <summary>Đọc xu hướng khung lớn. Hàm thuần: chỉ nhìn nến đã đóng được truyền vào.</summary>
    HtfTrendRead ReadTrend(IReadOnlyList<Candle> biasCandles, int pivotBars, int lookbackBars);

    /// <summary>
    /// Dựng các vùng giá trị nằm ĐÚNG PHÍA nhịp hồi: dưới giá khi xu hướng tăng, trên giá khi
    /// giảm. Sắp theo khoảng cách tới giá, gần nhất trước.
    /// </summary>
    IReadOnlyList<HtfValueZone> BuildValueZones(
        IReadOnlyList<Candle> biasCandles,
        HtfTrendRead trend,
        decimal price,
        decimal zoneHalfWidthAtr);
}

/// <summary>
/// Đọc cấu trúc khung 4 giờ theo cách một người đọc biểu đồ: chuỗi đỉnh/đáy trước, chỉ báo sau.
/// </summary>
/// <remarks>
/// <para><b>Vì sao cấu trúc đi trước chỉ báo.</b> Mọi phiên bản trước của engine xác định chiều
/// bằng chồng trung bình động. Trung bình động là hàm của giá QUÁ KHỨ, nên tại điểm ngoặt nó
/// luôn trả lời sai, và nó sai lâu đúng bằng chu kỳ của nó. Chuỗi đỉnh cao dần/đáy cao dần thì
/// không có độ trễ nào ngoài độ trễ xác nhận pivot — và độ trễ đó đo được, khai báo được.
/// Bằng chứng tại chỗ: phiếu ETH 71 điểm ngày 20/08 bị chặn vì MA7 vừa cắt xuống dưới MA25,
/// trong khi cấu trúc 4h lúc đó vẫn còn nguyên chuỗi đáy cao dần.</para>
///
/// <para><b>Vì sao chồng EMA vẫn có mặt.</b> Nó không quyết định chiều, chỉ phân biệt xu hướng
/// đã chín (ba lớp xếp đều) với xu hướng vừa mới hình thành. Khác biệt đó đi thẳng vào cỡ lệnh
/// chứ không đi vào quyết định vào hay không.</para>
///
/// <para><b>Không có "đi ngang" ở đây.</b> Khi chuỗi không nhất quán, kết quả là
/// <see cref="HtfTrend.Unclear"/> và bộ luật swing đứng ngoài, nhường ngày đó cho V6. Giả vờ
/// rằng "không đọc được" là "đi ngang" sẽ đẻ ra một playbook cho tình huống mà thật ra ta không
/// biết gì cả.</para>
/// </remarks>
public sealed class HtfSwingAnalyzer : IHtfSwingAnalyzer
{
    /// <summary>Chu kỳ EMA khung lớn. Là ĐỊNH NGHĨA của "chồng EMA", không phải ngưỡng để chỉnh.</summary>
    private const int EmaFast = 20;
    private const int EmaMid = 50;
    private const int EmaSlow = 200;
    private const int AtrPeriod = 14;

    /// <summary>Dải Fibonacci được coi là vùng hồi lành mạnh của một chân đẩy.</summary>
    /// <remarks>
    /// Nông hơn 38,2% thì chưa phải nhịp hồi, chỉ là một nến ngược. Sâu hơn 61,8% thì phần lớn
    /// chân đẩy đã bị trả lại và xác suất đây là đảo chiều chứ không phải nhịp hồi tăng nhanh.
    /// </remarks>
    private const decimal FibShallow = 0.382m;
    private const decimal FibDeep = 0.618m;

    private readonly ISwingDetector _swings;
    private readonly IIndicatorService _indicators;

    public HtfSwingAnalyzer(ISwingDetector swings, IIndicatorService indicators)
    {
        _swings = swings;
        _indicators = indicators;
    }

    public HtfTrendRead ReadTrend(IReadOnlyList<Candle> biasCandles, int pivotBars, int lookbackBars)
    {
        ArgumentNullException.ThrowIfNull(biasCandles);
        if (pivotBars <= 0) throw new ArgumentOutOfRangeException(nameof(pivotBars));

        var needed = 2 * pivotBars + 1;
        if (biasCandles.Count < needed)
            return HtfTrendRead.Unclear($"Chỉ có {biasCandles.Count} nến 4h, cần ít nhất {needed} để xác nhận điểm xoay.");

        // Cắt cửa sổ TRƯỚC khi dò pivot, để "xu hướng" luôn nói về cùng một khoảng thời gian
        // bất kể người gọi truyền vào bao nhiêu nến.
        var window = biasCandles.Count > lookbackBars && lookbackBars > 0
            ? biasCandles.Skip(biasCandles.Count - lookbackBars).ToList()
            : biasCandles;

        var pivots = _swings.Detect(window, pivotBars);
        var highs = pivots.Where(p => p.IsHigh).ToList();
        var lows = pivots.Where(p => !p.IsHigh).ToList();

        var closes = biasCandles.Select(c => c.Close).ToList();
        var ema20 = _indicators.Ema(closes, EmaFast);
        var ema50 = _indicators.Ema(closes, EmaMid);
        var ema200 = _indicators.Ema(closes, EmaSlow);
        var atr = _indicators.Atr(biasCandles, AtrPeriod);

        if (highs.Count < 2 || lows.Count < 2)
        {
            return HtfTrendRead.Unclear(
                $"Khung 4h mới có {highs.Count} đỉnh và {lows.Count} đáy đã xác nhận trong {window.Count} nến — "
                + "chưa đủ để nói về chuỗi.");
        }

        var lastHigh = highs[^1];
        var priorHigh = highs[^2];
        var lastLow = lows[^1];
        var priorLow = lows[^2];

        var up = lastHigh.Price > priorHigh.Price && lastLow.Price > priorLow.Price;
        var down = lastHigh.Price < priorHigh.Price && lastLow.Price < priorLow.Price;

        var lastConfirmedIndex = Math.Max(lastHigh.ConfirmedAtIndex, lastLow.ConfirmedAtIndex);
        var barsSince = window.Count - 1 - lastConfirmedIndex;

        if (!up && !down)
        {
            return HtfTrendRead.Unclear(
                $"Chuỗi 4h không nhất quán: đỉnh {priorHigh.Price:N2} → {lastHigh.Price:N2}, "
                + $"đáy {priorLow.Price:N2} → {lastLow.Price:N2}.")
                with { Ema20 = ema20, Ema50 = ema50, Ema200 = ema200, Atr = atr };
        }

        var trend = up ? HtfTrend.Up : HtfTrend.Down;

        var stacked = ema20 is { } f && ema50 is { } m && ema200 is { } s
            && (up ? f > m && m > s : f < m && m < s);
        var strength = stacked ? HtfTrendStrength.Aligned : HtfTrendStrength.Structural;

        // Mức làm hỏng cấu trúc: với xu hướng tăng đó là đáy cao hơn GẦN NHẤT. Đóng nến dưới nó
        // là đã mất một mắt của chuỗi, và chuỗi mất một mắt thì không còn là chuỗi.
        var invalidation = up ? lastLow.Price : lastHigh.Price;

        var detail =
            $"Khung 4h {(up ? "tăng" : "giảm")}: đỉnh {priorHigh.Price:N2} → {lastHigh.Price:N2}, "
            + $"đáy {priorLow.Price:N2} → {lastLow.Price:N2}"
            + (stacked ? "; chồng EMA 20/50/200 xếp cùng chiều" : "; chồng EMA chưa xếp theo")
            + $"; xác nhận cách đây {barsSince} nến.";

        return new HtfTrendRead(
            trend, strength,
            lastHigh.Price, lastLow.Price, priorHigh.Price, priorLow.Price,
            ema20, ema50, ema200, atr,
            invalidation, barsSince, detail);
    }

    public IReadOnlyList<HtfValueZone> BuildValueZones(
        IReadOnlyList<Candle> biasCandles,
        HtfTrendRead trend,
        decimal price,
        decimal zoneHalfWidthAtr)
    {
        ArgumentNullException.ThrowIfNull(biasCandles);
        ArgumentNullException.ThrowIfNull(trend);

        if (trend.Trend == HtfTrend.Unclear) return Array.Empty<HtfValueZone>();
        if (trend.Atr is not { } atr || atr <= 0m) return Array.Empty<HtfValueZone>();
        if (price <= 0m) return Array.Empty<HtfValueZone>();

        var isUp = trend.Trend == HtfTrend.Up;
        var half = atr * zoneHalfWidthAtr;
        if (half <= 0m) return Array.Empty<HtfValueZone>();

        var bands = new List<(decimal Low, decimal High, HtfZoneLayer Layer)>();

        void AddPoint(decimal? anchor, HtfZoneLayer layer)
        {
            if (anchor is not { } a || a <= 0m) return;
            // Chỉ nhận mức nằm đúng phía nhịp hồi. Một mức nằm phía bên kia giá không phải chỗ
            // để vào thuận xu hướng — nó là mục tiêu.
            if (isUp ? a > price : a < price) return;
            bands.Add((a - half, a + half, layer));
        }

        void AddBand(decimal? low, decimal? high, HtfZoneLayer layer)
        {
            if (low is not { } l || high is not { } h || l <= 0m || h <= 0m) return;
            if (l > h) (l, h) = (h, l);
            var anchor = isUp ? h : l;
            if (isUp ? anchor > price : anchor < price) return;
            bands.Add((l, h, layer));
        }

        // ── Lớp 1: đáy/đỉnh xoay khung lớn gần nhất cùng phía ──
        AddPoint(isUp ? trend.LastSwingLow : trend.LastSwingHigh, HtfZoneLayer.SwingLevel);

        // ── Lớp 2: mức đã đổi vai. Với xu hướng tăng, đỉnh cũ đã bị vượt qua giờ đỡ giá từ dưới ──
        AddPoint(isUp ? trend.PriorSwingHigh : trend.PriorSwingLow, HtfZoneLayer.FlippedLevel);

        // ── Lớp 3+4: trung bình động khung lớn ──
        AddPoint(trend.Ema20, HtfZoneLayer.Ema20);
        AddPoint(trend.Ema50, HtfZoneLayer.Ema50);

        // ── Lớp 5: dải Fibonacci của chân đẩy gần nhất ──
        if (trend.LastSwingLow is { } legLow && trend.LastSwingHigh is { } legHigh && legHigh > legLow)
        {
            var span = legHigh - legLow;
            // Chân đẩy đo từ đáy lên đỉnh với xu hướng tăng, và nhịp hồi trả lại từ ĐỈNH xuống.
            var shallow = isUp ? legHigh - span * FibShallow : legLow + span * FibShallow;
            var deep = isUp ? legHigh - span * FibDeep : legLow + span * FibDeep;
            AddBand(Math.Min(shallow, deep), Math.Max(shallow, deep), HtfZoneLayer.Fibonacci);
        }

        // ── Lớp 6: thân nến đã phá cấu trúc ──
        var breakoutBase = FindBreakoutBase(biasCandles, trend, isUp);
        if (breakoutBase is { } bb) AddBand(bb.Low, bb.High, HtfZoneLayer.BreakoutBase);

        if (bands.Count == 0) return Array.Empty<HtfValueZone>();

        return Merge(bands, price, isUp);
    }

    /// <summary>
    /// Tìm thân cây nến 4h đã đóng vượt qua đỉnh xoay trước đó — nơi khởi phát cú phá cấu trúc.
    /// </summary>
    /// <remarks>
    /// Dùng THÂN nến (mở–đóng) chứ không dùng cả bóng: bóng là nơi giá đã bị từ chối, thân là
    /// nơi giao dịch thật sự diễn ra. Vùng cầu nằm ở chỗ có người mua, không nằm ở chỗ có người
    /// thử bán rồi thất bại.
    /// </remarks>
    private static (decimal Low, decimal High)? FindBreakoutBase(
        IReadOnlyList<Candle> candles, HtfTrendRead trend, bool isUp)
    {
        var level = isUp ? trend.PriorSwingHigh : trend.PriorSwingLow;
        if (level is not { } lvl || lvl <= 0m) return null;

        for (var i = candles.Count - 1; i >= 1; i--)
        {
            var c = candles[i];
            var prev = candles[i - 1];
            var broke = isUp
                ? c.Close > lvl && prev.Close <= lvl
                : c.Close < lvl && prev.Close >= lvl;
            if (!broke) continue;

            var low = Math.Min(c.Open, c.Close);
            var high = Math.Max(c.Open, c.Close);
            return low >= high ? null : (low, high);
        }

        return null;
    }

    /// <summary>Gộp các dải chồng nhau thành vùng, hợp nhất danh sách lớp.</summary>
    private static List<HtfValueZone> Merge(
        List<(decimal Low, decimal High, HtfZoneLayer Layer)> bands, decimal price, bool isUp)
    {
        var ordered = bands.OrderBy(b => b.Low).ToList();
        var zones = new List<HtfValueZone>();

        var curLow = ordered[0].Low;
        var curHigh = ordered[0].High;
        var layers = new List<HtfZoneLayer> { ordered[0].Layer };

        for (var i = 1; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (b.Low <= curHigh)
            {
                curHigh = Math.Max(curHigh, b.High);
                if (!layers.Contains(b.Layer)) layers.Add(b.Layer);
                continue;
            }

            zones.Add(new HtfValueZone(curLow, curHigh, layers));
            curLow = b.Low;
            curHigh = b.High;
            layers = new List<HtfZoneLayer> { b.Layer };
        }

        zones.Add(new HtfValueZone(curLow, curHigh, layers));

        // Gần nhất trước: với xu hướng tăng, vùng có mép trên cao nhất là vùng giá gặp trước.
        return isUp
            ? zones.OrderByDescending(z => z.High).ToList()
            : zones.OrderBy(z => z.Low).ToList();
    }
}
