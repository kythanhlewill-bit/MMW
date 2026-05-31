using Microsoft.EntityFrameworkCore;

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
}
