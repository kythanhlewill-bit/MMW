using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring.Criteria;

/// <summary>
/// Chu kỳ chỉ báo dùng chung cho nhóm kỹ thuật.
/// </summary>
/// <remarks>
/// Nằm trong mã chứ không trong cấu hình vì chúng là ĐỊNH NGHĨA của "chồng EMA" và "khối lượng
/// trung bình", không phải khẩu vị rủi ro. Đổi 20/50/200 thành con số khác nghĩa là đổi sang
/// một chỉ báo khác, không phải chỉnh một tham số. Các ngưỡng ĐỂ CHỈNH nằm ở <c>EngineSetting</c>.
/// </remarks>
internal static class IndicatorPeriods
{
    public const int EmaFast = 20;
    public const int EmaMid = 50;
    public const int EmaSlow = 200;
    public const int VolumeSmaPeriod = 20;
    public const int RsiPeriod = 14;
    public const int AtrPeriod = 14;
}

// ─────────────────────────────────────────────────────────────────────────
// technical.htf_alignment — 10 điểm, veto cứng
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Đồng thuận thiên hướng khung 4 giờ, đọc từ chồng EMA 20/50/200.
/// </summary>
/// <remarks>
/// Veto cứng khi chồng EMA khung lớn đi NGƯỢC hẳn chiều mà kế hoạch ngày cho phép. Đây không
/// phải "điểm thấp" mà là mâu thuẫn giữa hai tầng: kế hoạch ngày nói chỉ được mua, còn khung
/// 4 giờ đang xếp giảm rõ. Vào lệnh trong tình trạng đó là chống lại chính hệ thống.
/// </remarks>
public sealed class HtfAlignmentCriterion : IScoreCriterion
{
    private readonly IIndicatorService _indicators;

    public HtfAlignmentCriterion(IIndicatorService indicators) => _indicators = indicators;

    public string Key => "technical.htf_alignment";
    public ScoreGroup Group => ScoreGroup.Technical;
    public int MaxPoints => 10;
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var closes = context.BiasCandles.Select(c => c.Close).ToList();

        var fast = _indicators.Ema(closes, IndicatorPeriods.EmaFast);
        var mid = _indicators.Ema(closes, IndicatorPeriods.EmaMid);
        var slow = _indicators.Ema(closes, IndicatorPeriods.EmaSlow);

        if (fast is null || mid is null)
        {
            return CriterionResult.Missing(
                $"Không đủ nến 4h để tính EMA{IndicatorPeriods.EmaFast}/{IndicatorPeriods.EmaMid} " +
                $"(có {context.BiasCandles.Count} nến).");
        }

        var bullish = fast > mid && (slow is null || mid > slow);
        var bearish = fast < mid && (slow is null || mid < slow);

        // Veto: khung lớn xếp ngược hẳn với chiều kế hoạch ngày cho phép.
        var planDirection = context.DailyPlan.AllowedDirections;
        if ((planDirection == AllowedDirections.LongOnly && bearish)
            || (planDirection == AllowedDirections.ShortOnly && bullish))
        {
            return CriterionResult.Veto(VetoReason.HtfMisaligned,
                $"Kế hoạch ngày cho {(planDirection == AllowedDirections.LongOnly ? "mua" : "bán")} " +
                $"nhưng chồng EMA 4h đang xếp {(bearish ? "giảm" : "tăng")} " +
                $"(EMA{IndicatorPeriods.EmaFast}={fast:N2}, EMA{IndicatorPeriods.EmaMid}={mid:N2}).");
        }

        var aligned = context.Direction == TradeDirection.Long ? bullish : bearish;
        var opposed = context.Direction == TradeDirection.Long ? bearish : bullish;

        if (aligned && slow is not null)
            return new CriterionResult(10, $"Chồng EMA 4h xếp đủ ba lớp thuận chiều lệnh (EMA{IndicatorPeriods.EmaFast}={fast:N2} / {mid:N2} / {slow:N2}).");

        if (aligned)
            return new CriterionResult(6, $"EMA{IndicatorPeriods.EmaFast} và EMA{IndicatorPeriods.EmaMid} thuận chiều nhưng chưa đủ nến cho EMA{IndicatorPeriods.EmaSlow}.");

        if (opposed)
            return new CriterionResult(0, $"Chồng EMA 4h xếp ngược chiều lệnh (EMA{IndicatorPeriods.EmaFast}={fast:N2}, EMA{IndicatorPeriods.EmaMid}={mid:N2}).");

        return new CriterionResult(3, $"Chồng EMA 4h đan xen, không nghiêng hẳn về phía nào (EMA{IndicatorPeriods.EmaFast}={fast:N2}, EMA{IndicatorPeriods.EmaMid}={mid:N2}).");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// technical.market_structure — 10 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Phá vỡ cấu trúc và kiểm định lại, tính trên nến vào lệnh.</summary>
public sealed class MarketStructureCriterion : IScoreCriterion
{
    private readonly MarketStructureAnalyzer _analyzer;
    private readonly IIndicatorService _indicators;

    public MarketStructureCriterion(MarketStructureAnalyzer analyzer, IIndicatorService indicators)
    {
        _analyzer = analyzer;
        _indicators = indicators;
    }

    public string Key => "technical.market_structure";
    public ScoreGroup Group => ScoreGroup.Technical;
    public int MaxPoints => 10;
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var atr = _indicators.Atr(context.EntryCandles, IndicatorPeriods.AtrPeriod);
        if (atr is null or <= 0m)
            return CriterionResult.Missing($"Không tính được biên độ dao động từ {context.EntryCandles.Count} nến vào lệnh.")
                with { StateCode = "Missing" };

        var result = _analyzer.Analyze(
            context.EntryCandles,
            context.Settings.SwingPivotBars,
            context.Settings.RetestWindowBars,
            atr.Value);

        var maxAge = context.Settings.PatternMaxAgeBars;
        var names = context.PriceAction.SupportingNames(context.Direction, maxAge);
        var confluence = names.Count == 0 ? "không có" : string.Join(", ", names);

        // Hợp lưu RÒNG, không phải "có mẫu hình thuận nào không". Một cú hai đáy đi kèm phân kỳ
        // RSI giảm là thị trường đang nói hai điều trái nhau — nó không đáng 8 điểm.
        var net = context.PriceAction.NetConfluence(context.Direction, maxAge);

        var wanted = context.Direction == TradeDirection.Long
            ? StructureBreak.BullishBreak
            : StructureBreak.BearishBreak;

        if (result.Break == StructureBreak.None)
        {
            var score = Blend(3, 8, net);
            return score > 3
                ? new CriterionResult(score, $"Chưa có BOS mới nhưng có hợp lưu price action thuận chiều: {confluence}.", StateCode: "NoBos")
                : new CriterionResult(3, net < 0m
                    ? $"Chưa có phá vỡ cấu trúc, và mẫu hình đang nghiêng NGƯỢC chiều lệnh (hợp lưu ròng {net:N2})."
                    : "Chưa có phá vỡ cấu trúc hoặc mẫu hình xác nhận trong cửa sổ đang xét.", StateCode: "NoBos");
        }

        if (result.Break != wanted)
            return new CriterionResult(0, $"Phá vỡ cấu trúc gần nhất đi ngược chiều lệnh ({result.Break}, mức {result.BrokenLevel:N2}).", StateCode: "OpposingBos");

        if (result.RetestFailed)
            return new CriterionResult(0, $"Đã phá vỡ mức {result.BrokenLevel:N2} nhưng kiểm định lại THẤT BẠI — giá đóng thủng trở lại.", StateCode: "RetestFailed");

        if (result.RetestConfirmed)
            return new CriterionResult(10, $"Phá vỡ mức {result.BrokenLevel:N2} thuận chiều và đã kiểm định lại thành công.", StateCode: "RetestConfirmed");

        var afterBreak = Blend(6, 8, net);
        return afterBreak > 6
            ? new CriterionResult(afterBreak, $"Phá vỡ mức {result.BrokenLevel:N2} thuận chiều, có hợp lưu {confluence}, chưa retest.", StateCode: "BosUnretested")
            : new CriterionResult(6, $"Phá vỡ mức {result.BrokenLevel:N2} thuận chiều, chưa kiểm định lại.", StateCode: "BosUnretested");
    }

    /// <summary>
    /// Điểm nằm giữa <paramref name="floor"/> và <paramref name="ceiling"/> theo hợp lưu ròng.
    /// </summary>
    /// <remarks>
    /// Đây là chỗ tuổi mẫu hình thật sự ăn vào điểm số. Trước V2 hợp lưu là một công tắc: một cú
    /// hai đáy phá neckline lúc 09:00 vẫn cho đủ 8/10 điểm cấu trúc lúc 17:00, đúng loại lệnh vào
    /// muộn mà <c>technical.entry_location</c> sinh ra để chặn. Nay mẫu hình càng cũ thì trọng số
    /// càng nhỏ và điểm trượt dần về sàn, thay vì rơi khỏi vách ở một mốc tuỳ ý.
    ///
    /// Kẹp về <c>[0, 1]</c> trước khi nội suy: hợp lưu ròng có thể lớn hơn 1 khi nhiều mẫu hình
    /// cùng thuận chiều, và cộng dồn tiếp sẽ vượt trần của chính tiêu chí.
    /// </remarks>
    private static int Blend(int floor, int ceiling, decimal netConfluence)
    {
        var weight = Math.Clamp(netConfluence, 0m, 1m);
        return floor + (int)Math.Round(weight * (ceiling - floor), MidpointRounding.AwayFromZero);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// technical.entry_location — 8 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Vị trí vào lệnh so với vùng giá trị (EMA20 và VWAP neo ngày).
/// </summary>
/// <remarks>
/// FR-027: giá đã chạy quá <c>MaxAtrFromConfirmation</c> lần biên độ khỏi vùng xác nhận thì
/// tiêu chí này nhận ĐÚNG 0 điểm, không phải điểm thấp. Đuổi theo giá là sai lầm đắt nhất mà
/// một hệ thống chấm điểm có thể hợp thức hoá bằng cách cho vài điểm an ủi.
/// </remarks>
public sealed class EntryLocationCriterion : IScoreCriterion
{
    private readonly IIndicatorService _indicators;

    public EntryLocationCriterion(IIndicatorService indicators) => _indicators = indicators;

    public string Key => "technical.entry_location";
    public ScoreGroup Group => ScoreGroup.Technical;
    public int MaxPoints => 8;

    /// <summary>Khoảng cách tới vùng giá trị không đổi theo chiều, nhưng hợp lưu Fibonacci thì có.</summary>
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var closes = context.EntryCandles.Select(c => c.Close).ToList();
        var ema = _indicators.Ema(closes, IndicatorPeriods.EmaFast);
        var vwap = _indicators.AnchoredVwap(context.EntryCandles);
        var atr = _indicators.Atr(context.EntryCandles, IndicatorPeriods.AtrPeriod);

        if (atr is null or <= 0m || (ema is null && vwap is null))
            return CriterionResult.Missing($"Thiếu dữ liệu tính vùng giá trị (có {context.EntryCandles.Count} nến vào lệnh).");

        // Vùng xác nhận là mốc GẦN giá nhất trong hai mốc — lấy mốc xa hơn sẽ khiến giá
        // trông như đang ở gần vùng giá trị trong khi nó đã bỏ xa một mốc.
        var anchors = new[] { ema, vwap }.Where(v => v is not null).Select(v => v!.Value).ToList();
        var nearest = anchors.OrderBy(a => Math.Abs(context.CurrentPrice - a)).First();

        var distanceAtr = Math.Abs(context.CurrentPrice - nearest) / atr.Value;
        var limit = context.Settings.MaxAtrFromConfirmation;

        if (distanceAtr > limit)
        {
            return new CriterionResult(0,
                $"Giá đã chạy {distanceAtr:N2} ATR khỏi vùng xác nhận, vượt trần {limit:N2} ATR — 0 điểm theo FR-027.");
        }

        var score = distanceAtr switch
        {
            <= 0.5m => 8,
            <= 1.0m => 5,
            _ => 2,
        };

        var inGoldenPocket = context.PriceAction.FibonacciConfluence(context.Direction);
        if (inGoldenPocket)
            score = Math.Min(MaxPoints, score + 2);

        return new CriterionResult(score,
            $"Giá cách vùng xác nhận {distanceAtr:N2} ATR (trần {limit:N2})" +
            (inGoldenPocket ? ", đồng thời nằm trong vùng hồi Fibonacci 50–61,8%." : "."));
    }
}

// ─────────────────────────────────────────────────────────────────────────
// technical.momentum — 7 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>RSI trong dải lành và biểu đồ MACD dốc thuận chiều.</summary>
public sealed class MomentumCriterion : IScoreCriterion
{
    private readonly IIndicatorService _indicators;

    public MomentumCriterion(IIndicatorService indicators) => _indicators = indicators;

    public string Key => "technical.momentum";
    public ScoreGroup Group => ScoreGroup.Technical;
    public int MaxPoints => 7;
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var closes = context.EntryCandles.Select(c => c.Close).ToList();

        var rsi = _indicators.Rsi(closes, IndicatorPeriods.RsiPeriod);
        var macdNow = _indicators.Macd(closes);
        var macdPrev = closes.Count > 1
            ? _indicators.Macd(closes.Take(closes.Count - 1).ToList())
            : new MacdResult(null, null, null);

        if (rsi is null || macdNow.Histogram is null || macdPrev.Histogram is null)
            return CriterionResult.Missing($"Không đủ nến để tính RSI và MACD (có {closes.Count}).");

        // Dải RSI phải SOI GƯƠNG theo chiều lệnh. Dải 45–65 mã hoá đúng một ý: "động lượng tăng
        // đã có nhưng chưa quá mua". Ảnh gương cho lệnh bán là 35–55. Áp nguyên 45–65 cho lệnh
        // bán làm đảo ngược ý nghĩa: RSI 40 (giảm lành mạnh, thứ ta MUỐN thấy) bị coi là ngoài
        // dải, còn RSI 63 (sát quá mua, bán vào sức mạnh) lại được điểm tối đa.
        //
        // Soi gương quanh 50 thay vì thêm hai trường cấu hình riêng: hai cặp ngưỡng độc lập sẽ
        // lệch nhau ngay lần chỉnh đầu tiên, và không có gì báo.
        var (lower, upper) = context.Direction == TradeDirection.Long
            ? (context.Settings.RsiLowerBound, context.Settings.RsiUpperBound)
            : (100m - context.Settings.RsiUpperBound, 100m - context.Settings.RsiLowerBound);

        var inBand = rsi >= lower && rsi <= upper;

        // "Dốc lên" theo chiều lệnh: mua thì biểu đồ phải tăng, bán thì phải giảm.
        var slopeOk = context.Direction == TradeDirection.Long
            ? macdNow.Histogram > macdPrev.Histogram
            : macdNow.Histogram < macdPrev.Histogram;

        var detail = $"RSI {rsi:N1} (dải {lower:N0}–{upper:N0}), biểu đồ MACD {macdPrev.Histogram:N4} → {macdNow.Histogram:N4}";

        var baseResult = (inBand, slopeOk) switch
        {
            (true, true) => new CriterionResult(7, $"Động lượng thuận cả hai mặt: {detail}."),
            (true, false) => new CriterionResult(4, $"RSI trong dải nhưng biểu đồ MACD chưa dốc thuận chiều: {detail}."),
            (false, true) => new CriterionResult(4, $"Biểu đồ MACD dốc thuận nhưng RSI ngoài dải: {detail}."),
            _ => new CriterionResult(0, $"Động lượng không ủng hộ: {detail}."),
        };

        // Kẹp về [-2, +2] rồi cộng MỘT lần. Cách cũ kiểm `Supports` trước rồi `Opposes` sau nên
        // khi cả hai cùng đúng, bằng chứng ngược chiều bị vứt trong im lặng và setup mâu thuẫn
        // được cộng đủ 2 điểm y như setup sạch.
        //
        // Hợp lưu ròng là số THẬP PHÂN vì mỗi mẫu hình mang trọng số theo tuổi; làm tròn ra xa 0
        // để một mẫu hình mới tinh (trọng số 1) vẫn đáng đúng một điểm như trước, còn một mẫu
        // hình đã đi được quá nửa đời thì không.
        var net = context.PriceAction.NetConfluence(context.Direction, context.Settings.PatternMaxAgeBars);
        var adjustment = Math.Clamp((int)Math.Round(net, MidpointRounding.AwayFromZero), -2, 2);
        if (adjustment == 0) return baseResult;

        var note = adjustment > 0
            ? $" Hợp lưu ròng +{net:N2}: mẫu hình/phân kỳ động lượng nghiêng thuận chiều."
            : $" Hợp lưu ròng {net:N2}: mẫu hình/phân kỳ động lượng nghiêng ngược chiều.";

        return baseResult with
        {
            AwardedPoints = Math.Clamp(baseResult.AwardedPoints + adjustment, 0, MaxPoints),
            Reason = baseResult.Reason + note,
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────
// technical.volume_confirmation — 5 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Khối lượng nến gần nhất so với trung bình 20 nến.</summary>
public sealed class VolumeConfirmationCriterion : IScoreCriterion
{
    private readonly IIndicatorService _indicators;

    public VolumeConfirmationCriterion(IIndicatorService indicators) => _indicators = indicators;

    public string Key => "technical.volume_confirmation";
    public ScoreGroup Group => ScoreGroup.Technical;
    public int MaxPoints => 5;

    /// <summary>Thân nến phải đóng THUẬN chiều lệnh, nên cùng một nến cho hai kết quả trái ngược.</summary>
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        if (context.EntryCandles.Count == 0)
            return CriterionResult.Missing("Không có nến vào lệnh nào.") with { StateCode = "Missing" };

        var average = _indicators.VolumeSma(context.EntryCandles, IndicatorPeriods.VolumeSmaPeriod);
        if (average is null or <= 0m)
            return CriterionResult.Missing($"Không tính được khối lượng trung bình {IndicatorPeriods.VolumeSmaPeriod} nến.")
                with { StateCode = "Missing" };

        var required = context.Settings.VolumeBreakoutMultiple;
        var minBody = context.Settings.MinCandleBodyRatio;
        var recentCount = Math.Min(3, context.EntryCandles.Count);
        var recent = context.EntryCandles.TakeLast(recentCount)
            .Select((candle, index) => new
            {
                Ratio = candle.Volume / average.Value,
                BarsAgo = recentCount - 1 - index,
                DirectionConfirmed = IsBodyConfirming(candle, context.Direction, minBody),
            })
            .ToList();
        var directional = recent.Where(x => x.DirectionConfirmed)
            .OrderByDescending(x => x.Ratio)
            .FirstOrDefault();
        var strongest = recent.OrderByDescending(x => x.Ratio).First();

        if (directional is not null && directional.Ratio >= required)
            return new CriterionResult(5,
                $"Trong 3 nến gần nhất có nến thuận chiều cách {directional.BarsAgo} nến với khối lượng " +
                $"gấp {directional.Ratio:N2} lần trung bình (cần {required:N2}).", StateCode: "StrongDirectional");

        if (strongest.Ratio >= required)
            return new CriterionResult(2,
                $"Khối lượng mạnh nhất 3 nến gấp {strongest.Ratio:N2} lần trung bình nhưng thân nến không xác nhận chiều lệnh.", StateCode: "StrongNonDirectional");

        if (directional is not null && directional.Ratio >= 1.0m)
            return new CriterionResult(3,
                $"Khối lượng nến thuận chiều mạnh nhất gấp {directional.Ratio:N2} lần trung bình, chưa đạt mức phá vỡ {required:N2}.", StateCode: "DirectionalAverage");

        return new CriterionResult(0,
            $"Ba nến gần nhất không có khối lượng thuận chiều đạt trung bình {IndicatorPeriods.VolumeSmaPeriod} nến.", StateCode: "Weak");
    }

    /// <summary>
    /// Nến có XÁC NHẬN chiều lệnh hay không: đúng dấu VÀ thân đủ dày.
    /// </summary>
    /// <remarks>
    /// Chỉ kiểm dấu <c>Close &gt; Open</c> là chưa đủ. Một nến doji đóng cao hơn mở 0,01 trên
    /// khối lượng gấp ba lần trung bình sẽ được 5/5 điểm, trong khi nó đang nói điều ngược lại:
    /// khối lượng lớn mà giá không đi đâu là dấu hiệu có người bán hết cỡ vào lực mua.
    ///
    /// Nến biên độ bằng 0 trả về false — không có thân thì không có xác nhận, và phép chia cho 0
    /// ở đây sẽ ném đúng vào giữa vòng chấm điểm.
    /// </remarks>
    private static bool IsBodyConfirming(Candle candle, TradeDirection direction, decimal minBodyRatio)
    {
        var sameSign = direction == TradeDirection.Long
            ? candle.Close > candle.Open
            : candle.Close < candle.Open;

        if (!sameSign) return false;

        var range = candle.High - candle.Low;
        if (range <= 0m) return false;

        return Math.Abs(candle.Close - candle.Open) / range >= minBodyRatio;
    }
}
