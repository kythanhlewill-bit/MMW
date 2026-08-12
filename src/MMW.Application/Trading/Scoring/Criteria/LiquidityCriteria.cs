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
// liquidity.zone_position — ĐÃ GỠ KHỎI THANG ĐIỂM (2026-08-12)
// ─────────────────────────────────────────────────────────────────────────
//
// Tiêu chí này chấm 5 điểm cho "vị trí cụm thanh khoản so với dừng lỗ và mục tiêu", xấp xỉ cụm
// bằng các đỉnh/đáy xoay. Đo trên dữ liệu chạy thật ngày 2026-08-12 thì nó trả về 0 điểm 90/102
// lần và 1 điểm 12 lần — CHƯA LẦN NÀO đạt 3 hay 5. Trung bình 0,12/5.
//
// Nguyên nhân là hai con số nhân nhau, và không con nào liên quan tới chất lượng setup:
//
//  - Tập điểm xoay quá dày. Ba khung (15m + 4h + 1D) được `Concat` lại, không gộp trùng, không
//    lọc độ mạnh, không lọc độ mới — 191–200 điểm xoay mỗi lần chấm.
//  - Dải "quét dừng lỗ" quá hẹp về giá trị tuyệt đối. Dải = 30% khoảng entry→stop, mà stop thực
//    tế chỉ rộng 0,2% giá: BTC ra 41 điểm (0,065% giá), ETH ra 1,68 USD (0,088% giá).
//
// Một cửa sổ 41 điểm giữa 200 điểm xoay thì gần như luôn tóm được một cái. Nhánh `huntZone` vì
// vậy gần như luôn đúng, và thang 0/1/3/5 co lại thành {0, 1}. Một tiêu chí trả gần như cùng một
// giá trị cho mọi đầu vào là HẰNG SỐ, không phải phép đo: nó không tách được setup tốt khỏi setup
// xấu, chỉ trừ đều 5 điểm của mọi phiếu và làm mẫu số 85 nói dối về số điểm thực sự với tới được.
//
// Gỡ chứ không sửa, vì phản ứng đúng với "có cụm ngay ngoài dừng lỗ" là DỜI dừng lỗ chứ không
// phải trừ điểm — và V2 đã làm đúng việc dời đó trong StructuralLevelPlanner. Giữ thêm một khoản
// trừ điểm cho cùng một tình huống là phạt hai lần một chuyện đã được xử lý.
//
// Muốn phục hồi (kèm gộp/lọc pivot, hoặc đổi dải quét sang theo ATR): `git show 7670943 --
// src/MMW.Application/Trading/Scoring/Criteria/LiquidityCriteria.cs`.

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
