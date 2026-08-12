using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring.Criteria;

// ─────────────────────────────────────────────────────────────────────────
// technical.structural_room — 0 điểm, veto cứng
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Cấu trúc có cho phép đặt dừng lỗ, và có đủ chỗ tới mục tiêu để trả nổi chi phí không.
/// </summary>
/// <remarks>
/// <b>Tiêu chí 0 điểm — nó là một cánh cổng, không phải một thang đo.</b> Đặt ở đây thay vì gộp
/// vào một tiêu chí có điểm vì hai lý do: nó không đụng vào tổng điểm (nên các ngưỡng không phải
/// tính lại), và nó bật ra thành một dòng phiếu riêng trả lời đúng một câu hỏi.
///
/// Veto ở hai tình huống, và cả hai đều là "sai rõ" chứ không phải "điểm thấp":
///
/// <para><b>1. Không đặt được dừng lỗ.</b> Điểm phủ định setup nằm xa hơn
/// <c>StopAtrMultipleMax</c>. Co size rồi vẫn vào là lặp lại cùng một sai lầm với chi phí thấp
/// hơn — nếu ta không đọc được cấu trúc thì lệnh dựa trên nó không nên tồn tại.</para>
///
/// <para><b>2. Không đủ chỗ chạy.</b> Tỉ lệ lãi/lỗ cấu trúc dưới <c>MinStructuralRr</c>. Đây là
/// hệ quả trực tiếp của toán chi phí chứ không phải một sở thích: với phí taker hai chiều cộng
/// trượt giá, một lệnh thua tốn khoảng 1,2–1,5R còn một lệnh thắng tại 1R chỉ thu về 0,6–0,8R.
/// Ở mục tiêu 1R, tỉ lệ thắng hoà vốn là <b>72%</b> — không bộ chấm điểm nào trên khung 15m đạt
/// được mức đó một cách bền vững. Vào một lệnh như thế không phải là chấp nhận rủi ro, mà là
/// trả phí để tung đồng xu.</para>
///
/// Đây cũng là chỗ sửa một mâu thuẫn cũ: tiêu chí <c>liquidity.zone_position</c> đã phát hiện
/// được "có cụm thanh khoản ngay ngoài dừng lỗ" và trả 0 điểm, nhưng phản ứng chỉ là trừ 5 điểm
/// rồi vẫn vào lệnh với đúng cái dừng lỗ đó. Nay dừng lỗ được DỜI theo cấu trúc, và nếu dời xong
/// mà không còn chỗ thì lệnh bị loại hẳn.
///
/// Chính vì việc dời đã được làm ở đây mà tiêu chí kia bị gỡ hẳn khỏi thang điểm ngày 2026-08-12:
/// giữ thêm một khoản trừ điểm cho tình huống đã được xử lý là phạt hai lần. Lý do đo đạc đầy đủ
/// nằm ở đầu tệp <c>Criteria/LiquidityCriteria.cs</c>.
/// </remarks>
public sealed class StructuralRoomCriterion : IScoreCriterion
{
    public string Key => "technical.structural_room";
    public ScoreGroup Group => ScoreGroup.Technical;

    /// <summary>Không đóng góp điểm — xem chú thích lớp.</summary>
    public int MaxPoints => 0;

    /// <summary>
    /// Mức dừng lỗ và mục tiêu được dựng RIÊNG cho từng chiều, nên cùng một cây nến có thể đủ chỗ
    /// chạy cho lệnh mua mà không đủ cho lệnh bán. Không đóng góp điểm nào vào phép so hai chiều,
    /// nhưng nó LOẠI hẳn một chiều khỏi cuộc so — đó mới là tác dụng thật của nó ở §4.
    /// </summary>
    public bool IsDirectional => true;

    public CriterionResult Evaluate(ScoringContext context)
    {
        var settings = context.Settings;

        if (context.StructuralLevels is not { } levels)
        {
            // Không có mức nào dựng được. Phân biệt hai nguyên nhân: thiếu nến/ATR là THIẾU DỮ
            // LIỆU, còn dựng được nhưng cấu trúc quá xa là một KẾT LUẬN. Gộp chung sẽ khiến
            // thống kê "lý do đứng ngoài" trộn lẫn lỗi hạ tầng với quyết định giao dịch.
            if (context.EntryCandles.Count == 0 || context.CurrentPrice <= 0m)
                return CriterionResult.Missing("Chưa đủ nến hoặc giá để dựng mức theo cấu trúc.");

            return CriterionResult.Veto(VetoReason.InsufficientRoom,
                $"Điểm phủ định setup nằm xa hơn trần {settings.StopAtrMultipleMax:N2} ATR — " +
                "không đặt được dừng lỗ hợp lệ, và co size không sửa được điều đó.");
        }

        var required = settings.MinStructuralRr;

        if (levels.RiskReward < required)
        {
            return CriterionResult.Veto(VetoReason.InsufficientRoom,
                $"R:R cấu trúc chỉ {levels.RiskReward:N2}, dưới mức tối thiểu {required:N2} " +
                $"(dừng lỗ {levels.StopLoss:N2}, mục tiêu {levels.TakeProfit:N2}) — " +
                "khoảng chạy không đủ trả phí một vòng lệnh.");
        }

        return new CriterionResult(0, levels.ReasonVi, IsApproximation: !levels.TargetIsStructural);
    }
}
