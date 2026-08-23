using MMW.Domain.Entities;

namespace MMW.Web.Models;

public class DashboardViewModel
{
    public long? SelectedAccountId { get; set; }
    public string? AccountName { get; set; }
    public string Currency { get; set; } = "USDT";
    public decimal Balance { get; set; }
    public decimal? LiveBalance { get; set; }
    public string? LiveBalanceFetchError { get; set; }

    public int TotalTrades { get; set; }
    public int OpenTrades { get; set; }
    public int ClosedTrades { get; set; }
    public int WinTrades { get; set; }
    public int LossTrades { get; set; }
    public decimal TotalPnl { get; set; }
    public int TotalFlags { get; set; }
    public int CriticalFlags { get; set; }

    /// <summary>
    /// Kết quả tách theo nhóm lệnh. Các con số gộp ở trên vẫn giữ, nhưng đây mới là chỗ đọc
    /// được hệ nào đang lãi.
    /// </summary>
    public IReadOnlyList<TradeStyleStats> StyleStats { get; set; } = Array.Empty<TradeStyleStats>();

    public IReadOnlyList<TradingAccount> Accounts { get; set; } = new List<TradingAccount>();

    // ── Trạng thái kỷ luật của engine (T125, FR-035) ────────────────────

    /// <summary>
    /// Kết quả từng rào chắn kỷ luật tại thời điểm mở trang. Rỗng khi tài khoản chưa có
    /// cấu hình engine hoặc chưa có kế hoạch ngày.
    /// </summary>
    /// <remarks>
    /// Hiện cả rào đang cho qua chứ không chỉ rào đang chặn: biết "năm rào đã kiểm và đều ổn"
    /// là thông tin khác hẳn với việc không thấy gì.
    /// </remarks>
    public IReadOnlyList<DisciplineStatusRow> DisciplineGates { get; set; } = Array.Empty<DisciplineStatusRow>();

    /// <summary>Đúng khi có rào đang chặn — mọi lệnh mới bị từ chối.</summary>
    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }

    /// <summary>Hệ số kích thước hiện hành do các rào áp đặt. Luôn ≤ 1.0.</summary>
    public decimal DisciplineSizeMultiplier { get; set; } = 1.0m;
}

/// <param name="Reason">Số liệu thực tế so với ngưỡng (Nguyên tắc I).</param>
public sealed record DisciplineStatusRow(string Key, string Action, string Reason, bool IsBlocking);
