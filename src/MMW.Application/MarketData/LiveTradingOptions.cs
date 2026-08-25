namespace MMW.Application.MarketData;

/// <summary>
/// Cấu hình GIAO DỊCH THẬT. Mặc định an toàn (tắt) — phải bật rõ ràng trong
/// User Secrets / appsettings mới đặt lệnh thật.
/// </summary>
public class LiveTradingOptions
{
    public const string SectionName = "LiveTrading";

    /// <summary>Công tắc tổng. false = KHÔNG bao giờ gửi lệnh lên sàn. Kill-switch.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>true = dùng Binance Futures Testnet (tiền ảo). Đặt false để chạy tiền thật.</summary>
    public bool UseTestnet { get; set; } = true;

    /// <summary>Cap cứng đòn bẩy. Lệnh vượt → bị chặn.</summary>
    public int MaxLeverage { get; set; } = 20;

    /// <summary>Đòn bẩy mặc định khi trade không nhập leverage.</summary>
    public int DefaultLeverage { get; set; } = 20;

    /// <summary>Giá trị danh nghĩa tối thiểu Binance Futures yêu cầu cho lệnh mở mới.</summary>
    public decimal MinOrderNotionalUsdt { get; set; } = 20m;

    /// <summary>Cap cứng giá trị danh nghĩa (Entry×Qty) một lệnh, USDT. Vượt → chặn.</summary>
    public decimal MaxNotionalUsdt { get; set; } = 50m;

    /// <summary>Cap cứng số lệnh live gửi trong 1 ngày.</summary>
    public int MaxOrdersPerDay { get; set; } = 10;

    /// <summary>Trần lệnh live/ngày RIÊNG cho nhóm swing khung 4 giờ.</summary>
    /// <remarks>
    /// Đếm riêng vì hai bộ luật chạy song song tiêu hai ngân sách khác nhau. Dùng chung thì bộ
    /// swing — vốn chỉ vào vài lệnh mỗi ngày — vẫn có thể bị bộ trong ngày đẩy hết suất, hoặc
    /// ngược lại, mà lý do chặn không nhắc gì tới nhóm kia.
    /// </remarks>
    public int MaxOrdersPerDayHtf { get; set; } = 4;
}
