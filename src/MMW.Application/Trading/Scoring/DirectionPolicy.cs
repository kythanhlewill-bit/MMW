using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring;

/// <summary>
/// Biên độ gần nhất và vị trí của giá bên trong nó.
/// </summary>
/// <param name="Percent">
/// <c>0</c> = đúng đáy biên độ, <c>100</c> = đúng đỉnh. KHÔNG kẹp: dưới 0 hoặc trên 100 nghĩa là
/// giá đã ra ngoài biên độ, và đó là một kết luận khác hẳn "đang ở sát biên".
/// </param>
public sealed record RangeLocation(decimal Low, decimal High, decimal Percent, int PivotCount);

/// <summary>Các chiều còn được phép, sau khi lọc theo kế hoạch ngày VÀ vị trí trong biên độ.</summary>
/// <param name="Veto">Khác null ⟹ không chiều nào đi tiếp; <paramref name="Detail"/> nói vì sao.</param>
/// <param name="Excluded">
/// Chiều được kế hoạch ngày cho phép nhưng bị VỊ TRÍ loại. Vẫn được chấm — chỉ để ghi vào phiếu.
/// </param>
/// <remarks>
/// <paramref name="Excluded"/> tồn tại vì một lý do đo lường, không phải vì quyết định. Trên ngày
/// đi ngang, vị trí trong biên độ chốt chiều TRƯỚC khi chấm, nên phép so điểm không còn hai ứng
/// viên — và nếu không ghi lại điểm của chiều bị loại thì sau này không có cách nào
/// trả lời câu "quy tắc biên độ có đang chọn nhầm bên không". Đó đúng là loại câu hỏi mà lần
/// backtest kế tiếp phải trả lời được, và thước đo phải tồn tại TRƯỚC lần chạy đó.
/// </remarks>
public sealed record DirectionCandidates(
    IReadOnlyList<TradeDirection> Allowed,
    RangeLocation? Range,
    VetoReason? Veto,
    string? Detail,
    IReadOnlyList<TradeDirection>? Excluded = null)
{
    public IReadOnlyList<TradeDirection> ExcludedOrEmpty => Excluded ?? Array.Empty<TradeDirection>();
}

public interface IDirectionPolicy
{
    /// <summary>Hàm THUẦN: không I/O, không đồng hồ.</summary>
    DirectionCandidates Candidates(
        DailyPlan plan, EngineSetting settings, IReadOnlyList<Candle> biasCandles, decimal price);

    /// <summary>Biên độ đọc từ pivot khung thiên hướng đã xác nhận. Null khi không dựng được.</summary>
    RangeLocation? Locate(IReadOnlyList<Candle> biasCandles, int pivotBars, decimal price);
}

/// <summary>
/// Quyết định những chiều nào được phép TRƯỚC khi chấm điểm.
/// </summary>
/// <remarks>
/// <para><b>Vì sao lọc trước chứ không chấm rồi lọc.</b> Trên ngày đi ngang, engine cũ chọn chiều
/// bằng một phép so EMA 20/50 khung 4 giờ. Nhưng chính bộ chấm điểm khai báo ngày đi ngang "chỉ
/// hợp setup đảo chiều tại biên", còn EMA 20/50 trên một ngày đi ngang thì đan xen và gần như
/// ngẫu nhiên — chiều lệnh về bản chất là tung đồng xu. Vị trí trong biên độ là một sự kiện đo
/// được, EMA cắt nhau trong vùng đi ngang thì không.</para>
///
/// <para><b>Định nghĩa biên độ, chốt tại đây để không ai phải đoán.</b> Bản nháp V2 viết "biên độ
/// 20 phiên" mà không nói phiên nào, khung nào, tính lúc nào — ba khoảng trống, mỗi cái đủ để
/// sinh ra một lỗi nhìn trước. Định nghĩa dùng:</para>
///
/// <list type="bullet">
/// <item>Nguồn: nến <b>khung thiên hướng</b> (4h) ĐÃ ĐÓNG của chính mã đang xét, đúng chuỗi mà
/// bối cảnh chấm điểm đang mang.</item>
/// <item>Cửa sổ: <see cref="RangeLookbackBars"/> nến cuối.</item>
/// <item>Biên: đỉnh xoay cao nhất và đáy xoay thấp nhất trong cửa sổ, chỉ tính pivot ĐÃ XÁC NHẬN
/// (<see cref="ISwingDetector"/> có độ trễ N nến của R-007, nên mọi pivot nó trả về đều đã biết
/// được tại thời điểm chấm).</item>
/// <item>Thời điểm: ngay lúc chấm, so với giá hiện tại.</item>
/// </list>
///
/// <para><b>Vì sao không dùng 20 phiên NGÀY.</b> Đó là cửa sổ mà <c>DayRegimeClassifier</c> dùng
/// để gọi tên cấu trúc BTC, và nó đúng cho việc đó. Nhưng 20 phiên ngày là ba tuần: biên độ dựng
/// từ đó rộng tới mức "sát biên" gần như không bao giờ xảy ra trên một lệnh giữ 1–4 tiếng, và
/// ràng buộc sẽ không lọc gì ngoài việc xoá sổ mọi lệnh ngày range. Năm ngày giao dịch là biên độ
/// mà giá ĐANG ở trong, không phải một biên độ đã kết thúc từ hai tuần trước.</para>
/// </remarks>
public sealed class DirectionPolicy : IDirectionPolicy
{
    /// <summary>
    /// Số nến khung thiên hướng dựng nên biên độ. 30 nến 4h = 5 ngày giao dịch.
    /// </summary>
    /// <remarks>
    /// Là ĐỊNH NGHĨA của "biên độ giá đang ở trong", không phải khẩu vị rủi ro — cùng loại với
    /// <c>StopLookbackBars</c> của bộ dựng mức và <c>StructureLookbackDays</c> của bộ phân loại
    /// ngày. Ngưỡng ĐỂ CHỈNH là <c>RangeEdgePercent</c>, và nó nằm ở <c>EngineSetting</c>.
    /// </remarks>
    public const int RangeLookbackBars = 30;

    private static readonly TradeDirection[] Both = { TradeDirection.Long, TradeDirection.Short };
    private static readonly TradeDirection[] LongOnly = { TradeDirection.Long };
    private static readonly TradeDirection[] ShortOnly = { TradeDirection.Short };
    private static readonly TradeDirection[] None = Array.Empty<TradeDirection>();

    private readonly ISwingDetector _swings;

    public DirectionPolicy(ISwingDetector swings) => _swings = swings;

    public DirectionCandidates Candidates(
        DailyPlan plan, EngineSetting settings, IReadOnlyList<Candle> biasCandles, decimal price)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(biasCandles);

        var planned = Allowed(plan.AllowedDirections);

        if (planned.Length == 0)
        {
            return new DirectionCandidates(None, null, VetoReason.DirectionNotAllowed,
                $"Kế hoạch ngày {plan.PlanDateUtc:dd/MM} không cho phép chiều nào.");
        }

        // Ràng buộc vị trí CHỈ áp cho ngày đi ngang. Trên ngày trend, "biên" của một biên độ đang
        // bị phá vỡ không phải chỗ để fade — và chiều đã bị kế hoạch ngày khoá lại từ trước.
        if (plan.DayRegime != DayRegime.Range)
            return new DirectionCandidates(planned, null, null, null);

        var range = Locate(biasCandles, settings.SwingPivotBars, price);

        // V6 phải để cả hai chiều đi tới detector M15: tam giác đối xứng chỉ có hướng sau khi
        // breakout đóng, còn rectangle fade tự chặn theo đúng biên. Dùng biên 4h để veto tại đây
        // sẽ xoá setup compression trước khi trigger có cơ hội nhìn thấy nó.
        if (settings.StrategyVersion.UsesSidewaysV6())
            return new DirectionCandidates(planned, range, null,
                "V6 chuyển quyền chọn chiều ngày Range cho detector Rectangle/Triangle M15.");

        if (range is null)
        {
            return new DirectionCandidates(None, null, VetoReason.InsufficientData,
                $"Ngày đi ngang nhưng không dựng được biên độ từ {biasCandles.Count} nến khung thiên hướng — " +
                "không biết đâu là biên thì không có gì để fade.");
        }

        var edge = settings.RangeEdgePercent;

        var byLocation =
            range.Percent > 100m || range.Percent < 0m ? None
            : range.Percent >= 100m - edge ? ShortOnly
            : range.Percent <= edge ? LongOnly
            : None;

        if (byLocation.Length == 0)
        {
            var where = range.Percent > 100m ? "đã vượt lên trên biên độ"
                : range.Percent < 0m ? "đã thủng xuống dưới biên độ"
                : "nằm giữa biên độ";

            return new DirectionCandidates(None, range, VetoReason.NotAtRangeEdge,
                $"Ngày đi ngang, giá {price:N2} {where} [{range.Low:N2} – {range.High:N2}] " +
                $"(vị trí {range.Percent:N1}%, cần ≤ {edge:N0}% hoặc ≥ {100m - edge:N0}%) — " +
                "vào lệnh giữa vùng đi ngang là cách nhanh nhất để thua trên loại ngày này.");
        }

        var allowed = byLocation.Where(planned.Contains).ToArray();

        if (allowed.Length == 0)
        {
            return new DirectionCandidates(None, range, VetoReason.DirectionNotAllowed,
                $"Vị trí trong biên độ ({range.Percent:N1}%) chỉ hợp lệnh {Describe(byLocation[0])}, " +
                $"nhưng kế hoạch ngày cho {Describe(plan.AllowedDirections)}.");
        }

        return new DirectionCandidates(
            allowed, range, null, null, planned.Where(d => !allowed.Contains(d)).ToArray());
    }

    public RangeLocation? Locate(IReadOnlyList<Candle> biasCandles, int pivotBars, decimal price)
    {
        ArgumentNullException.ThrowIfNull(biasCandles);
        if (biasCandles.Count == 0 || price <= 0m) return null;

        var window = biasCandles.TakeLast(RangeLookbackBars).ToList();
        var pivots = _swings.Detect(window, Math.Max(1, pivotBars));

        var high = pivots.Where(p => p.IsHigh).Select(p => (decimal?)p.Price).Max();
        var low = pivots.Where(p => !p.IsHigh).Select(p => (decimal?)p.Price).Min();

        if (high is not { } top || low is not { } bottom || top <= bottom) return null;

        return new RangeLocation(bottom, top, (price - bottom) / (top - bottom) * 100m, pivots.Count);
    }

    private static TradeDirection[] Allowed(AllowedDirections directions) => directions switch
    {
        AllowedDirections.LongOnly => LongOnly,
        AllowedDirections.ShortOnly => ShortOnly,
        AllowedDirections.Both => Both,
        _ => None,
    };

    private static string Describe(TradeDirection d) => d == TradeDirection.Long ? "mua" : "bán";

    private static string Describe(AllowedDirections d) => d switch
    {
        AllowedDirections.LongOnly => "chỉ mua",
        AllowedDirections.ShortOnly => "chỉ bán",
        AllowedDirections.Both => "cả hai chiều",
        _ => "không chiều nào",
    };
}

/// <summary>Chiều được chọn, kèm đủ số liệu để trả lời "vì sao chiều này chứ không phải chiều kia".</summary>
/// <param name="OppositeScore">Null khi chiều kia không được chấm, hoặc được chấm nhưng bị veto cứng.</param>
/// <param name="Margin">Chênh lệch điểm để chẩn đoán; không còn là gate sau bằng chứng âm #23/#24.</param>
public sealed record DirectionChoice(
    TradeDirection Direction,
    ScoringOutcome Score,
    ScoringOutcome? OppositeScore,
    int? Margin,
    string ReasonVi);

/// <summary>
/// Chọn giữa hai chiều đã chấm. Thuần và tất định.
/// </summary>
/// <remarks>
/// <para><b>Biên bắt buộc.</b> Hai chiều chấm gần bằng nhau nghĩa là thị trường chưa nói gì; chọn
/// bên nhỉnh hơn vài điểm trên một hệ thống có kỳ vọng âm là tung đồng xu CÓ TRẢ PHÍ. Bỏ phiếu
/// trắng là một tính năng, không phải một lệnh bị bỏ lỡ.</para>
///
/// <para><b>So bằng điểm ĐỔI THEO CHIỀU.</b> Các tiêu chí không đổi theo chiều cho hai bên đúng
/// cùng số điểm, nên chúng triệt tiêu trong phép trừ — về mặt số học, so tổng cho ra cùng một
/// hiệu. Con số riêng vẫn được dùng và được ghi lại vì nó làm ngưỡng có nghĩa đọc được: 8 điểm
/// trên thang <c>DirectionalMaxPoints</c>, chứ không phải 8 điểm trên thang tổng mà phần lớn không
/// liên quan gì tới chiều.</para>
///
/// <para><b>Một chiều bị veto không được đem ra so.</b> Veto cứng làm vòng chấm dừng giữa chừng,
/// nên điểm của chiều đó là một tổng dở dang chứ không phải một điểm thấp. Đem nó vào phép trừ là
/// so hai con số không cùng đơn vị. Chiều bị veto bị LOẠI, và khi chỉ còn một ứng viên thì không
/// có biên nào để đòi hỏi — chiều kia không thua điểm, nó bị cấm.</para>
/// </remarks>
public static class DirectionSelector
{
    public static DirectionChoice Select(
        IReadOnlyList<(TradeDirection Direction, ScoringOutcome Score)> scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        if (scored.Count == 0)
            throw new ArgumentException("Phải có ít nhất một chiều đã chấm.", nameof(scored));

        var live = scored.Where(s => !s.Score.IsVetoed).ToList();

        // Không chiều nào sống: trả về ứng viên ĐẦU TIÊN theo thứ tự tất định do caller truyền
        // vào. Phiếu sẽ nêu đúng lý do veto của chiều đó thay vì bịa ra một lý do tổng hợp.
        if (live.Count == 0)
        {
            var first = scored[0];
            return new DirectionChoice(first.Direction, first.Score, null, null,
                $"Mọi chiều ứng viên đều bị veto cứng; ghi lý do của chiều {Describe(first.Direction)}.");
        }

        if (live.Count == 1)
        {
            var only = live[0];
            return new DirectionChoice(only.Direction, only.Score, null, null,
                scored.Count == 1
                    ? $"Kế hoạch ngày và vị trí giá chỉ để lại chiều {Describe(only.Direction)}."
                    : $"Chiều {Describe(Opposite(only.Direction))} bị veto cứng, chỉ còn chiều {Describe(only.Direction)}.");
        }

        // Hoà điểm thì lấy theo thứ tự enum để kết quả tất định. A/B #23/#24 cho thấy biên 8 chỉ
        // chặn thật 5 setup trên 1.270 trade và làm expectancy/DD hơi xấu hơn, nên gate bị bỏ.
        var ordered = live
            .OrderByDescending(s => s.Score.DirectionalScore)
            .ThenBy(s => (int)s.Direction)
            .ToList();

        var best = ordered[0];
        var other = ordered[1];
        var margin = best.Score.DirectionalScore - other.Score.DirectionalScore;
        var comparison =
            $"{Describe(best.Direction)} {best.Score.DirectionalScore} so với " +
            $"{Describe(other.Direction)} {other.Score.DirectionalScore} " +
            $"trên thang {best.Score.DirectionalMaxPoints} điểm đổi theo chiều (chênh {margin})";

        return new DirectionChoice(
            best.Direction,
            best.Score,
            other.Score,
            margin,
            $"Chọn chiều {Describe(best.Direction)}: {comparison}. Biên veto đã bị loại sau A/B #23/#24.");
    }

    private static TradeDirection Opposite(TradeDirection d) =>
        d == TradeDirection.Long ? TradeDirection.Short : TradeDirection.Long;

    private static string Describe(TradeDirection d) => d == TradeDirection.Long ? "mua" : "bán";
}
