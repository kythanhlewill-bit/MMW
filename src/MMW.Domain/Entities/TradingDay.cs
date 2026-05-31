using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>
/// Tổng hợp theo ngày của một tài khoản — nền cho overtrade, daily loss limit và phân tích chuỗi thua.
/// Có thể tính lại (recompute) từ Trades; lưu sẵn để query/dashboard nhanh.
/// </summary>
public class TradingDay : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    public DateOnly Date { get; set; }

    public int TradeCount { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }

    [Precision(18, 8)] public decimal GrossProfit { get; set; }
    [Precision(18, 8)] public decimal GrossLoss { get; set; }
    [Precision(18, 8)] public decimal NetPnl { get; set; }

    /// <summary>Số lệnh thua liên tiếp lớn nhất trong ngày.</summary>
    public int MaxConsecutiveLosses { get; set; }

    /// <summary>Tổng % rủi ro đã đặt trong ngày.</summary>
    [Precision(9, 4)] public decimal TotalRiskPercent { get; set; }

    /// <summary>Vốn đầu ngày (để tính % lỗ ngày).</summary>
    [Precision(18, 8)] public decimal? StartingEquity { get; set; }

    public ICollection<Flag> Flags { get; set; } = new List<Flag>();
}
