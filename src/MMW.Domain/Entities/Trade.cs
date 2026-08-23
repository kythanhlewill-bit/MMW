using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Lệnh giao dịch — trung tâm của Trade Journal.
/// Ngoài dữ liệu giá/khối lượng còn lưu tâm lý và các chỉ số rủi ro đã tính sẵn để query/thống kê.
/// </summary>
public class Trade : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    /// <summary>Setup/chiến lược đã dùng (tuỳ chọn).</summary>
    public long? StrategyId { get; set; }
    public Strategy? Strategy { get; set; }

    /// <summary>
    /// Phiếu chấm điểm đã sinh ra lệnh này. Null với lệnh nhập tay hoặc lệnh từ đường AI cũ.
    /// Liên kết ngược này là thứ cho phép hỏi "lệnh thua hôm qua điểm bao nhiêu, tiêu chí nào yếu".
    /// </summary>
    public long? EntryScorecardId { get; set; }

    [Required, MaxLength(30)]
    public string Symbol { get; set; } = null!;

    public TradeDirection Direction { get; set; }
    public TradeStatus Status { get; set; } = TradeStatus.Planned;
    public TradeSource Source { get; set; } = TradeSource.Manual;
    public OrderType OrderType { get; set; } = OrderType.Market;

    /// <summary>
    /// Nhóm lệnh theo khung ra quyết định. Lệnh nhập tay mặc định là lệnh ngắn.
    /// </summary>
    /// <remarks>
    /// Lưu thẳng vào lệnh chứ không suy ra từ phiếu chấm điểm, vì phần lớn truy vấn báo cáo đi
    /// từ bảng này và lệnh nhập tay thì không có phiếu nào để suy ra cả.
    /// </remarks>
    public TradeStyle Style { get; set; } = TradeStyle.Intraday;

    // --- Giá & khối lượng ---
    [Precision(18, 8)] public decimal EntryPrice { get; set; }
    [Precision(18, 8)] public decimal? ExitPrice { get; set; }
    [Precision(18, 8)] public decimal? StopLoss { get; set; }
    [Precision(18, 8)] public decimal? TakeProfit { get; set; }
    [Precision(18, 8)] public decimal Quantity { get; set; }
    [Precision(9, 4)] public decimal? Leverage { get; set; } = 20m;
    [Precision(18, 8)] public decimal Fee { get; set; }
    [Precision(18, 8)] public decimal? RealizedPnl { get; set; }

    // --- Chốt lời hai phần: chốt một nửa ở mục tiêu gần, giữ phần còn lại chạy ---

    /// <summary>
    /// Mục tiêu GẦN, nơi đóng một phần vị thế. Null nghĩa là lệnh chỉ có một mục tiêu duy nhất.
    /// </summary>
    /// <remarks>
    /// <see cref="TakeProfit"/> luôn là mục tiêu CUỐI. Trước khi có trường này, đường chạy thật
    /// đặt đúng một lệnh chốt lời cỡ đầy đủ ngay tại mục tiêu cuối, nên mọi lệnh không tới đích
    /// đều quay về chạm dừng lỗ và mất trọn 1R — kể cả những lệnh đã đi đúng hướng hơn nửa
    /// đường. Đó là chỗ rò rỉ lớn nhất của bộ luật cũ.
    /// </remarks>
    [Precision(18, 8)] public decimal? FirstTakeProfit { get; set; }

    /// <summary>Phần vị thế đóng tại <see cref="FirstTakeProfit"/>, theo tỉ lệ 0–1.</summary>
    [Precision(9, 4)] public decimal? FirstTakeProfitFraction { get; set; }

    /// <summary>Khối lượng đã tính sẵn cho lệnh chốt phần đầu, sau khi làm tròn theo bước của sàn.</summary>
    [Precision(18, 8)] public decimal? FirstTakeProfitQuantity { get; set; }

    /// <summary>Thời điểm phần đầu thật sự khớp trên sàn. Null nghĩa là chưa chạm.</summary>
    public DateTime? FirstTargetFilledAt { get; set; }

    /// <summary>
    /// Dừng lỗ BAN ĐẦU, giữ nguyên kể cả sau khi dừng lỗ đã được kéo lên.
    /// </summary>
    /// <remarks>
    /// <see cref="StopLoss"/> đổi giá trị mỗi lần kéo, nên nếu tính R từ nó thì một lệnh được
    /// kéo về hoà vốn sẽ hiện ra như lệnh có rủi ro bằng không — chia cho số không. R phải đo
    /// bằng rủi ro đã CHẤP NHẬN lúc vào, và đây là nơi giữ nó.
    /// </remarks>
    [Precision(18, 8)] public decimal? InitialStopLoss { get; set; }

    /// <summary>Số nến mỗi bên dùng để tìm pivot khi kéo dừng lỗ. 0 = không kéo.</summary>
    public int TrailPivotBars { get; set; }

    /// <summary>Số lần dừng lỗ đã được kéo. Chỉ để đọc nhật ký, không tham gia tính toán.</summary>
    public int TrailUpdateCount { get; set; }

    // --- Chỉ số rủi ro tính sẵn (do service/Rule Engine ghi) ---
    /// <summary>Tiền rủi ro = |Entry - StopLoss| * Quantity.</summary>
    [Precision(18, 8)] public decimal? RiskAmount { get; set; }

    /// <summary>RiskAmount / vốn tại thời điểm vào lệnh, theo %.</summary>
    [Precision(9, 4)] public decimal? RiskPercent { get; set; }

    /// <summary>Reward:Risk dự kiến = |TakeProfit - Entry| / |Entry - StopLoss|.</summary>
    [Precision(9, 4)] public decimal? PlannedRiskReward { get; set; }

    /// <summary>Kết quả theo R = RealizedPnl / RiskAmount.</summary>
    [Precision(9, 4)] public decimal? RMultiple { get; set; }

    public TradeOutcome? Outcome { get; set; }

    // --- Tâm lý (journaling) ---
    public EmotionState EmotionBefore { get; set; } = EmotionState.Unknown;
    public EmotionState EmotionAfter { get; set; } = EmotionState.Unknown;

    // --- Thời gian ---
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // --- Meta ---
    [MaxLength(2000)] public string? Note { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }
    /// <summary>Mã lệnh từ sàn khi import (chống trùng).</summary>
    [MaxLength(100)] public string? ExternalId { get; set; }

    // --- Live trading (đặt lệnh thật lên sàn) ---
    /// <summary>True nếu lệnh đã được gửi thật lên sàn (không chỉ ghi nhật ký).</summary>
    public bool IsLive { get; set; }
    public LiveOrderStatus LiveStatus { get; set; } = LiveOrderStatus.None;
    /// <summary>orderId entry do sàn trả về.</summary>
    [MaxLength(100)] public string? ExchangeOrderId { get; set; }
    /// <summary>clientOrderId tự sinh để chống đặt trùng (idempotency).</summary>
    [MaxLength(100)] public string? ExchangeClientOrderId { get; set; }
    /// <summary>Ghi chú trạng thái live (lý do block / message lỗi).</summary>
    [MaxLength(500)] public string? LiveNote { get; set; }

    // Navigation
    public ICollection<TradeTag> Tags { get; set; } = new List<TradeTag>();
    public ICollection<Flag> Flags { get; set; } = new List<Flag>();
}
