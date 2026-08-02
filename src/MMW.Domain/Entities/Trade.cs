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

    // --- Giá & khối lượng ---
    [Precision(18, 8)] public decimal EntryPrice { get; set; }
    [Precision(18, 8)] public decimal? ExitPrice { get; set; }
    [Precision(18, 8)] public decimal? StopLoss { get; set; }
    [Precision(18, 8)] public decimal? TakeProfit { get; set; }
    [Precision(18, 8)] public decimal Quantity { get; set; }
    [Precision(9, 4)] public decimal? Leverage { get; set; } = 20m;
    [Precision(18, 8)] public decimal Fee { get; set; }
    [Precision(18, 8)] public decimal? RealizedPnl { get; set; }

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
