using MMW.Domain.Entities;

namespace MMW.Application.Behavior;

/// <summary>
/// Dữ liệu cho phân tích hành vi của một lệnh. History là các lệnh TRƯỚC đó cùng tài khoản,
/// sắp theo thời gian TĂNG DẦN, không gồm lệnh hiện tại.
/// </summary>
public sealed class BehaviorContext
{
    public required Trade Trade { get; init; }
    public required RiskSetting Settings { get; init; }
    public required IReadOnlyList<Trade> History { get; init; }

    /// <summary>Mốc thời gian dùng để xếp chuỗi lệnh.</summary>
    public static DateTime Timeline(Trade t) => t.OpenedAt ?? t.CreatedDate;

    /// <summary>Lệnh đã có kết quả (đã đóng).</summary>
    public static bool IsClosed(Trade t) => t.RealizedPnl.HasValue;

    /// <summary>Lệnh thua (nguồn sự thật: RealizedPnl &lt; 0).</summary>
    public static bool IsLoss(Trade t) => t.RealizedPnl is < 0m;
}
