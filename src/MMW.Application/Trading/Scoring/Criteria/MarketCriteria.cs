using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring.Criteria;

// ─────────────────────────────────────────────────────────────────────────
// market.day_regime_match — 10 điểm, veto cứng
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Chiều lệnh có khớp trạng thái ngày không (FR-021).
/// </summary>
/// <remarks>
/// Veto cứng khi chiều không nằm trong danh sách kế hoạch ngày cho phép. Dùng lại đúng
/// <see cref="DailyPlanGate"/> của tầng 1 thay vì viết lại phép so sánh: hai bản sao của cùng
/// một quy tắc sẽ lệch nhau, và lệch ở đây nghĩa là một tầng cho qua thứ mà tầng kia cấm.
/// </remarks>
public sealed class DayRegimeMatchCriterion : IScoreCriterion
{
    public string Key => "market.day_regime_match";
    public ScoreGroup Group => ScoreGroup.Market;
    public int MaxPoints => 10;
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var plan = context.DailyPlan;

        var veto = DailyPlanGate.Check(plan, context.Direction);
        if (veto is not null)
        {
            return CriterionResult.Veto(veto.Value,
                $"Kế hoạch ngày {plan.PlanDateUtc:dd/MM} cho phép {Describe(plan.AllowedDirections)}, " +
                $"tối đa {plan.MaxTradesToday} lệnh — lệnh {Describe(context.Direction)} không qua được.");
        }

        var trendMatches =
            (plan.DayRegime == DayRegime.TrendUp && context.Direction == TradeDirection.Long)
            || (plan.DayRegime == DayRegime.TrendDown && context.Direction == TradeDirection.Short);

        if (trendMatches)
            return new CriterionResult(10, $"Lệnh thuận xu hướng ngày ({plan.DayRegime}).");

        return plan.DayRegime switch
        {
            DayRegime.Range => new CriterionResult(6, "Ngày đi ngang — chỉ hợp setup đảo chiều tại biên."),
            DayRegime.HighVolatility => new CriterionResult(4, $"Ngày biến động cực đoan ({plan.VolatilityRegime}); chiều được phép nhưng bối cảnh xấu."),
            DayRegime.EventDay => new CriterionResult(4, "Ngày có tin tác động cao; chiều được phép nhưng bối cảnh xấu."),
            _ => new CriterionResult(4, $"Chiều được phép nhưng không thuận xu hướng ngày ({plan.DayRegime})."),
        };
    }

    private static string Describe(AllowedDirections d) => d switch
    {
        AllowedDirections.LongOnly => "chỉ mua",
        AllowedDirections.ShortOnly => "chỉ bán",
        AllowedDirections.Both => "cả hai chiều",
        _ => "không chiều nào",
    };

    private static string Describe(TradeDirection d) => d == TradeDirection.Long ? "mua" : "bán";
}

// ─────────────────────────────────────────────────────────────────────────
// market.volatility_regime — 6 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phân vị biến động của ngày. Dải giữa được điểm tối đa.
/// </summary>
/// <remarks>
/// Cả quá yên lẫn quá dữ đều bị trừ, và đó là điểm khác biệt so với trực giác thông thường:
/// biến động cao trông như cơ hội lớn, nhưng với khung giữ lệnh 1–4 tiếng thì nó chủ yếu là
/// dừng lỗ bị quét.
/// </remarks>
public sealed class VolatilityRegimeCriterion : IScoreCriterion
{
    public string Key => "market.volatility_regime";
    public ScoreGroup Group => ScoreGroup.Market;
    public int MaxPoints => 6;

    /// <summary>Biến động của ngày không biết ta đang định mua hay bán.</summary>
    public bool IsDirectional => false;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var percentile = context.DailyPlan.AtrPercentile;
        if (percentile is null)
            return CriterionResult.Missing("Kế hoạch ngày không có phân vị biến động (thiếu dữ liệu lịch sử).");

        var low = context.Settings.VolatilitySweetSpotLow;
        var high = context.Settings.VolatilitySweetSpotHigh;

        if (percentile >= low && percentile <= high)
            return new CriterionResult(6, $"Phân vị biến động {percentile:N1} nằm trong dải lý tưởng {low:N0}–{high:N0}.");

        return context.DailyPlan.VolatilityRegime switch
        {
            VolatilityRegime.Extreme => new CriterionResult(0, $"Phân vị biến động {percentile:N1} — vùng cực đoan."),
            VolatilityRegime.High => new CriterionResult(2, $"Phân vị biến động {percentile:N1} — cao hơn dải lý tưởng {low:N0}–{high:N0}."),
            VolatilityRegime.Low => new CriterionResult(2, $"Phân vị biến động {percentile:N1} — thấp hơn dải lý tưởng {low:N0}–{high:N0}."),
            _ => new CriterionResult(3, $"Phân vị biến động {percentile:N1} — ngoài dải lý tưởng {low:N0}–{high:N0}."),
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────
// market.session_quality — 6 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Điểm chất lượng khung giờ, lấy nguyên từ tầng chặn theo khung giờ (US1).</summary>
/// <remarks>
/// Thang của <c>SessionQuality</c> vốn đã là 0–6 nên không cần quy đổi. Nếu về sau thang đổi,
/// chỗ này phải đổi theo — và test bảng phiên sẽ đỏ trước.
/// </remarks>
public sealed class SessionQualityCriterion : IScoreCriterion
{
    public string Key => "market.session_quality";
    public ScoreGroup Group => ScoreGroup.Market;
    public int MaxPoints => 6;

    /// <summary>Chất lượng khung giờ là tính chất của ĐỒNG HỒ, không phải của chiều lệnh.</summary>
    public bool IsDirectional => false;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var quality = context.SessionQuality;
        if (quality is null)
            return CriterionResult.Missing("Không tính được điểm chất lượng khung giờ.");

        var source = quality.IsPersonalised
            ? $"thống kê của bạn, {quality.SampleSize} lệnh"
            : "bảng chuẩn";

        return new CriterionResult(
            Math.Clamp(quality.Score, 0, MaxPoints),
            $"Khung {quality.Label}: {quality.Score}/6 ({source}).");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// market.leader_correlation — 4 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Tương quan với mã dẫn dắt, chỉ có ý nghĩa khi giao dịch mã không phải BTC.</summary>
public sealed class LeaderCorrelationCriterion : IScoreCriterion
{
    public string Key => "market.leader_correlation";
    public ScoreGroup Group => ScoreGroup.Market;
    public int MaxPoints => 4;

    /// <summary>
    /// Tương quan đo mức ĐỒNG PHA, không đo hướng. Một mã bám sát mã dẫn dắt thì bám cả khi
    /// giảm — nên hệ số này cho cùng một câu trả lời cho lệnh mua và lệnh bán.
    /// </summary>
    public bool IsDirectional => false;

    public CriterionResult Evaluate(ScoringContext context)
    {
        // Giao dịch chính mã dẫn dắt thì không có rủi ro lệch pha nào để trừ. Trả "thiếu dữ
        // liệu" ở đây sẽ phạt BTC 4 điểm mỗi lần chấm vì một rủi ro nó không mang.
        if (context.IsLeaderSymbol)
            return new CriterionResult(4, $"{context.Symbol} chính là mã dẫn dắt — không có rủi ro lệch pha.");

        var correlation = context.LeaderCorrelation;
        if (correlation is null)
            return CriterionResult.Missing("Không tính được tương quan với mã dẫn dắt.");

        var strong = context.Settings.LeaderCorrelationStrong;
        var value = correlation.Value;

        if (value >= strong)
            return new CriterionResult(4, $"Tương quan với mã dẫn dắt {value:N2} (≥ {strong:N2}) — đi cùng pha rõ.");

        if (value >= strong / 2m)
            return new CriterionResult(2, $"Tương quan với mã dẫn dắt {value:N2}, dưới mức đồng pha rõ {strong:N2}.");

        return new CriterionResult(0, $"Tương quan với mã dẫn dắt chỉ {value:N2} — chuyển động rời rạc, khó dựa vào bối cảnh chung.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// market.funding_crowding — 4 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phí vốn cực đoan CÙNG CHIỀU lệnh thì bị trừ.
/// </summary>
/// <remarks>
/// Phí vốn dương cao nghĩa là bên mua đang trả tiền để giữ vị thế — đám đông đã chật một phía,
/// và phía chật là phía dễ bị quét. Vào lệnh mua lúc đó là xếp hàng cùng chỗ với những người
/// sắp bị đẩy ra. Phí vốn cực đoan NGƯỢC chiều lệnh thì ngược lại, là điểm cộng tự nhiên.
/// </remarks>
public sealed class FundingCrowdingCriterion : IScoreCriterion
{
    public string Key => "market.funding_crowding";
    public ScoreGroup Group => ScoreGroup.Market;
    public int MaxPoints => 4;

    /// <summary>Cùng một mức phí vốn cực đoan là điểm trừ cho một chiều và điểm cộng cho chiều kia.</summary>
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        if (context.Funding is null)
            return CriterionResult.Missing("Không lấy được phí vốn.");

        var rate = context.Funding.LastFundingRate;
        var threshold = context.Settings.ExtremeFundingRate;

        var crowdedLong = rate >= threshold;
        var crowdedShort = rate <= -threshold;

        var sameSide = (context.Direction == TradeDirection.Long && crowdedLong)
                       || (context.Direction == TradeDirection.Short && crowdedShort);

        if (sameSide)
        {
            return new CriterionResult(0,
                $"Phí vốn {rate:P4} vượt ngưỡng cực đoan {threshold:P4} và nghiêng CÙNG chiều lệnh — đám đông đã chật phía này.");
        }

        if (crowdedLong || crowdedShort)
        {
            return new CriterionResult(4,
                $"Phí vốn {rate:P4} cực đoan nhưng nghiêng NGƯỢC chiều lệnh — đám đông chật ở phía đối diện.");
        }

        return new CriterionResult(4, $"Phí vốn {rate:P4} trong vùng bình thường (ngưỡng cực đoan {threshold:P4}).");
    }
}
