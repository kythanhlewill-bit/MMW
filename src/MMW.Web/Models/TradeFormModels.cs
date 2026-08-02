using System.ComponentModel.DataAnnotations;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Models;

public class CreateTradeForm
{
    /// <summary>0 = tạo mới; &gt;0 = đang sửa lệnh này.</summary>
    public long Id { get; set; }

    [Required(ErrorMessage = "Chọn tài khoản")]
    [Display(Name = "Tài khoản")]
    public long TradingAccountId { get; set; }

    [Display(Name = "Chiến lược")]
    public long? StrategyId { get; set; }

    [Required(ErrorMessage = "Nhập symbol")]
    [Display(Name = "Symbol")]
    public string Symbol { get; set; } = "";

    [Display(Name = "Hướng")]
    public TradeDirection Direction { get; set; } = TradeDirection.Long;

    [Display(Name = "Loại lệnh")]
    public OrderType OrderType { get; set; } = OrderType.Market;

    [Display(Name = "Trạng thái")]
    public TradeStatus Status { get; set; } = TradeStatus.Open;

    [Display(Name = "Giá vào")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá vào phải > 0")]
    public decimal EntryPrice { get; set; }

    [Display(Name = "Stop Loss")]
    public decimal? StopLoss { get; set; }

    [Display(Name = "Take Profit")]
    public decimal? TakeProfit { get; set; }

    [Display(Name = "Khối lượng")]
    [Range(0, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Display(Name = "Đòn bẩy")]
    public decimal? Leverage { get; set; } = 20m;

    [Display(Name = "Phí")]
    public decimal Fee { get; set; }

    [Display(Name = "Tâm lý trước khi vào")]
    public EmotionState EmotionBefore { get; set; } = EmotionState.Unknown;

    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }
}

public class CreateTradeViewModel
{
    public CreateTradeForm Form { get; set; } = new();
    public IReadOnlyList<TradingAccount> Accounts { get; set; } = new List<TradingAccount>();
    public IReadOnlyList<Strategy> Strategies { get; set; } = new List<Strategy>();
    public IReadOnlyList<string> Symbols { get; set; } = new List<string>();
    public IReadOnlyList<AccountRiskInfo> AccountRisks { get; set; } = new List<AccountRiskInfo>();

    /// <summary>Nhãn nguồn điền sẵn (vd "đề xuất #5").</summary>
    public string? FromHint { get; set; }
}

/// <summary>Dữ liệu để JS tự tính khối lượng theo % rủi ro.</summary>
public class AccountRiskInfo
{
    public long Id { get; set; }
    public decimal Balance { get; set; }
    public decimal MaxRiskPercent { get; set; }
    /// <summary>RR tối thiểu — dùng để gợi ý giá TP từ khoảng cách SL.</summary>
    public decimal Rr { get; set; }
}

public class CloseTradeForm
{
    public long TradeId { get; set; }

    // Hiển thị (read-only)
    public string Symbol { get; set; } = "";
    public TradeDirection Direction { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }

    [Display(Name = "Giá thoát")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá thoát phải > 0")]
    public decimal ExitPrice { get; set; }

    [Display(Name = "Tâm lý khi đóng")]
    public EmotionState EmotionAfter { get; set; } = EmotionState.Unknown;
}
