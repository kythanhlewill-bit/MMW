using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Ngưỡng cấu hình cho Rule Engine + Behavior detection (1:1 với TradingAccount).
/// Toàn bộ logic phát hiện đọc ngưỡng từ đây — không hardcode.
/// </summary>
public class RiskSetting : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    // --- Rule Engine ---
    /// <summary>% rủi ro tối đa mỗi lệnh trên tổng vốn (mặc định 1%).</summary>
    [Precision(9, 4)]
    public decimal MaxRiskPerTradePercent { get; set; } = 1m;

    /// <summary>Tỷ lệ Reward:Risk tối thiểu chấp nhận (mặc định 1.5).</summary>
    [Precision(9, 4)]
    public decimal MinRiskRewardRatio { get; set; } = 1.5m;

    /// <summary>Số lệnh tối đa mỗi ngày (mặc định 5).</summary>
    public int MaxTradesPerDay { get; set; } = 5;

    /// <summary>% lỗ tối đa mỗi ngày trên vốn (mặc định 3%).</summary>
    [Precision(9, 4)]
    public decimal MaxDailyLossPercent { get; set; } = 3m;

    // --- Hạn mức RIÊNG cho nhóm lệnh swing khung 4 giờ ---
    //
    // Hai bộ luật chạy song song trên một tài khoản có ký quỹ tách được (ví USDT / ví USDC),
    // nhưng các bộ đếm "trong ngày" thì không tách theo. Dùng chung nghĩa là bộ swing thua một
    // lệnh là bộ trong ngày đứng luôn tới nửa đêm UTC — một bộ luật bị khoá bởi kết quả của bộ
    // luật khác, và điều đó xảy ra âm thầm, không có lý do nào nhắc tới nhóm kia.
    //
    // Đây đúng là cái bẫy mà trần vị thế đồng thời đã tránh được từ trước (MaxConcurrentPositions
    // với V7MaxConcurrentSwingPositions đếm riêng). Ba hạn mức còn lại nay theo cùng nguyên tắc.

    /// <summary>
    /// Số lệnh swing tối đa mỗi ngày. Đếm riêng, không dùng chung với lệnh trong ngày.
    /// </summary>
    /// <remarks>
    /// Mặc định 2 chứ không phải 5: nhịp hồi khung 4 giờ mỗi ngày chỉ có vài cơ hội, và một hạn
    /// mức rộng ở đây không mở thêm cơ hội nào — nó chỉ mở đường cho việc vào lại cùng một ý
    /// tưởng khi cấu trúc 4h chưa kịp đổi.
    /// </remarks>
    public int MaxTradesPerDayHtf { get; set; } = 2;

    /// <summary>% lỗ tối đa mỗi ngày của riêng nhóm swing.</summary>
    /// <remarks>
    /// Dừng nhóm swing KHÔNG dừng nhóm trong ngày, và ngược lại. Mỗi bộ luật chịu trách nhiệm
    /// cho kết quả của chính nó.
    /// </remarks>
    [Precision(9, 4)]
    public decimal MaxDailyLossPercentHtf { get; set; } = 3m;

    /// <summary>% rủi ro tối đa mỗi lệnh SWING trên vốn.</summary>
    /// <remarks>
    /// Mặc định 1% và cố ý KHÔNG bám theo giá trị của nhóm lệnh ngắn. Bộ luật swing có thể chưa
    /// từng chạy thật lần nào trong khi nhóm ngắn đã có lịch sử để biện minh cho cỡ lệnh của nó;
    /// thừa hưởng con số đó nghĩa là lệnh swing ĐẦU TIÊN vào bằng cỡ mà nhóm ngắn phải chạy hàng
    /// chục lệnh mới dám dùng.
    /// </remarks>
    [Precision(9, 4)]
    public decimal MaxRiskPerTradePercentHtf { get; set; } = 1m;

    /// <summary>Số lệnh swing thua liên tiếp thì dừng nhóm swing đến hết ngày UTC.</summary>
    /// <remarks>
    /// Đếm riêng là bắt buộc chứ không phải cho gọn: hai bộ luật đọc chiều từ hai nguồn khác
    /// nhau, nên chuỗi thua của bên này không mang thông tin gì về bên kia. Gộp chung thì một
    /// chuỗi thua của nhóm ngắn dừng luôn nhóm swing dù cấu trúc 4h chưa hề sai.
    /// </remarks>
    public int LossStreakThresholdHtf { get; set; } = 3;

    /// <summary>Bắt buộc có Stop Loss.</summary>
    public bool RequireStopLoss { get; set; } = true;

    // --- Behavior detection ---
    /// <summary>Vào lệnh trong vòng N phút sau khi vừa cắt lỗ → nghi revenge trade.</summary>
    public int RevengeTradeWindowMinutes { get; set; } = 30;

    /// <summary>Số lệnh thua liên tiếp coi là chuỗi thua (tilt risk).</summary>
    public int LossStreakThreshold { get; set; } = 3;

    /// <summary>% tăng kích thước lệnh so với trung bình → nghi tilt/oversize.</summary>
    [Precision(9, 4)]
    public decimal TiltSizeIncreasePercent { get; set; } = 50m;

    // ── Chọn ngưỡng theo nhóm ───────────────────────────────────────────
    //
    // Mọi phép rẽ "nhóm này thì đọc cột kia" nằm ở đây và chỉ ở đây. Rải câu điều kiện đó ra
    // từng nơi gọi là cách chắc chắn để một nơi bị bỏ sót, và một hạn mức bỏ sót không nổ ra
    // lỗi — nó chỉ âm thầm áp ngưỡng của nhóm khác.

    /// <summary>% rủi ro mỗi lệnh của nhóm tương ứng.</summary>
    public decimal MaxRiskPerTradePercentOf(TradeStyle style) =>
        style == TradeStyle.HtfSwing ? MaxRiskPerTradePercentHtf : MaxRiskPerTradePercent;

    /// <summary>Ngưỡng dừng ngày theo chuỗi thua của nhóm tương ứng.</summary>
    public int LossStreakThresholdOf(TradeStyle style) =>
        style == TradeStyle.HtfSwing ? LossStreakThresholdHtf : LossStreakThreshold;

    /// <summary>Số lệnh tối đa mỗi ngày của nhóm tương ứng.</summary>
    public int MaxTradesPerDayOf(TradeStyle style) =>
        style == TradeStyle.HtfSwing ? MaxTradesPerDayHtf : MaxTradesPerDay;

    /// <summary>% lỗ tối đa mỗi ngày của nhóm tương ứng.</summary>
    public decimal MaxDailyLossPercentOf(TradeStyle style) =>
        style == TradeStyle.HtfSwing ? MaxDailyLossPercentHtf : MaxDailyLossPercent;
}
