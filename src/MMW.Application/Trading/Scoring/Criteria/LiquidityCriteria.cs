using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring.Criteria;

// ─────────────────────────────────────────────────────────────────────────
// liquidity.open_interest — 5 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thay đổi lượng hợp đồng mở trong 4 giờ gần nhất.
/// </summary>
/// <remarks>
/// Lượng hợp đồng mở TĂNG cùng với giá đi thuận chiều nghĩa là tiền mới đang vào theo hướng
/// đó. GIẢM nghĩa là chuyển động hiện tại chủ yếu do đóng vị thế cũ — một cú đi lên vì bên bán
/// bỏ chạy chứ không vì bên mua tin tưởng, và loại chuyển động đó hết đà nhanh.
/// </remarks>
public sealed class OpenInterestCriterion : IScoreCriterion
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(4);

    public string Key => "liquidity.open_interest";
    public ScoreGroup Group => ScoreGroup.Liquidity;
    public int MaxPoints => 5;

    /// <summary>
    /// Chỉ đo lượng hợp đồng mở TĂNG hay GIẢM, không ghép với hướng giá — nên nó cho cùng một
    /// câu trả lời cho cả hai chiều. Ghép thêm hướng giá là một thay đổi về nghiệp vụ, không
    /// phải một dòng khai báo; khi nào làm thì đổi cờ này cùng lúc.
    /// </summary>
    public bool IsDirectional => false;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var change = context.OpenInterest?.ChangePercent(Window);
        if (change is null)
            return CriterionResult.Missing("Không đủ dữ liệu lượng hợp đồng mở phủ hết 4 giờ gần nhất.");

        var strong = context.Settings.OpenInterestStrongChangePercent;
        var value = change.Value;

        if (value >= strong)
            return new CriterionResult(5, $"Lượng hợp đồng mở 4h tăng {value:N2}% (ngưỡng mạnh {strong:N2}%) — tiền mới đang vào.");

        if (value > 0m)
            return new CriterionResult(3, $"Lượng hợp đồng mở 4h tăng nhẹ {value:N2}%, chưa đạt ngưỡng mạnh {strong:N2}%.");

        if (value > -strong)
            return new CriterionResult(2, $"Lượng hợp đồng mở 4h giảm nhẹ {value:N2}%.");

        return new CriterionResult(0, $"Lượng hợp đồng mở 4h giảm {value:N2}% — chuyển động chủ yếu do đóng vị thế cũ.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// liquidity.zone_position — 5 điểm, LUÔN là xấp xỉ
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Vị trí các cụm thanh khoản so với mức chốt lời và dừng lỗ dự kiến.
/// </summary>
/// <remarks>
/// Luôn đặt <c>IsApproximation = true</c> theo R-010, và đó không phải hình thức. Cụm thanh
/// khoản thật nằm trong sổ lệnh của sàn, thứ không có API công khai nào cho xem đầy đủ. Ở đây
/// chúng được XẤP XỈ bằng các đỉnh/đáy xoay gần nhất — nơi dừng lỗ của số đông thường nằm.
/// Đánh dấu là xấp xỉ để về sau, khi so sánh kiểm thử lịch sử với chạy thật, không ai nhầm
/// con số này với một phép đo.
///
/// Cụm nằm NGAY NGOÀI dừng lỗ là trường hợp xấu nhất: giá chỉ cần chạm tới đó là quét sạch
/// lệnh rồi quay đầu, và setup đúng vẫn thua.
/// </remarks>
public sealed class LiquidityZoneCriterion : IScoreCriterion
{
    /// <summary>
    /// Cụm nằm trong khoảng này (tính theo phần khoảng cách entry→dừng lỗ) kể từ dừng lỗ thì
    /// coi là "ngay ngoài". Là ĐỊNH NGHĨA của phép xấp xỉ, không phải khẩu vị rủi ro.
    /// </summary>
    private const decimal StopHuntBandRatio = 0.3m;

    private readonly ISwingDetector _swings;

    public LiquidityZoneCriterion(ISwingDetector swings) => _swings = swings;

    public string Key => "liquidity.zone_position";
    public ScoreGroup Group => ScoreGroup.Liquidity;
    public int MaxPoints => 5;
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        if (context.PlannedStopLoss is not { } stop || context.PlannedTakeProfit is not { } target)
            return CriterionResult.Missing("Chưa có mức dừng lỗ và chốt lời dự kiến để đối chiếu cụm thanh khoản.");

        var pivotBars = Math.Max(1, context.Settings.SwingPivotBars);

        // Gộp cả ba khung. Trước V2 chỉ khung vào lệnh được nhìn, nên kháng cự 4h và mức ngày
        // VÔ HÌNH với engine — và vào lệnh mua ngay dưới đỉnh 4h là kịch bản thua kinh điển.
        // Dữ liệu đã nằm sẵn trong bối cảnh từ đầu, chỉ là chưa ai đọc.
        var pivots = _swings.Detect(context.EntryCandles, pivotBars)
            .Concat(_swings.Detect(context.BiasCandles, pivotBars))
            .Concat(_swings.Detect(context.DailyCandles, pivotBars))
            .ToList();

        if (pivots.Count == 0)
            return CriterionResult.Missing("Không tìm được điểm xoay nào để xấp xỉ cụm thanh khoản.");

        var entry = context.CurrentPrice;
        var isLong = context.Direction == TradeDirection.Long;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
            return CriterionResult.Missing("Khoảng cách tới dừng lỗ bằng 0 — không đối chiếu được.");

        var clusters = pivots.Select(p => p.Price).ToList();

        // Cụm nằm ngay ngoài dừng lỗ — nơi giá bị hút tới để quét lệnh.
        var band = risk * StopHuntBandRatio;
        var huntZone = clusters.Any(c => isLong
            ? c <= stop && c >= stop - band
            : c >= stop && c <= stop + band);

        if (huntZone)
        {
            return new CriterionResult(0,
                $"Có cụm thanh khoản ngay ngoài dừng lỗ {stop:N2} (trong {StopHuntBandRatio:P0} khoảng rủi ro) — rủi ro bị quét trước khi chạy.",
                IsApproximation: true);
        }

        // Cụm chắn giữa đường tới mục tiêu — giá thường dừng lại ở đó.
        var blocking = clusters.Count(c => isLong
            ? c > entry && c < target
            : c < entry && c > target);

        var score = blocking switch
        {
            0 => 5,
            1 => 3,
            _ => 1,
        };

        return new CriterionResult(score,
            $"{blocking} cụm thanh khoản (xấp xỉ từ {pivots.Count} điểm xoay) nằm giữa giá {entry:N2} và mục tiêu {target:N2}.",
            IsApproximation: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// liquidity.spread_depth — 5 điểm
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Chênh lệch mua-bán và độ sâu sổ lệnh.</summary>
public sealed class SpreadDepthCriterion : IScoreCriterion
{
    public string Key => "liquidity.spread_depth";
    public ScoreGroup Group => ScoreGroup.Liquidity;
    public int MaxPoints => 5;

    /// <summary>Chênh lệch mua-bán là chi phí của cả hai chiều như nhau.</summary>
    public bool IsDirectional => false;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var spread = context.Depth?.SpreadBps;
        if (spread is null)
            return CriterionResult.Missing("Không lấy được sổ lệnh, hoặc một bên sổ lệnh rỗng.");

        var limit = context.Settings.MaxSpreadBps;
        var value = spread.Value;

        if (value <= limit)
            return new CriterionResult(5, $"Chênh lệch mua-bán {value:N2} điểm cơ bản (trần {limit:N2}).");

        if (value <= limit * 2m)
            return new CriterionResult(3, $"Chênh lệch mua-bán {value:N2} điểm cơ bản, gấp tới {value / limit:N1} lần trần {limit:N2}.");

        return new CriterionResult(0, $"Chênh lệch mua-bán {value:N2} điểm cơ bản, quá rộng so với trần {limit:N2} — chi phí vào lệnh ăn mòn kỳ vọng.");
    }
}
