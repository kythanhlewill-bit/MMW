using MMW.Application.Abstractions;

namespace MMW.RuleEngine.Tests;

/// <summary>Đồng hồ giả cho test — thời gian do test đặt, không phụ thuộc máy chạy.</summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }

    /// <summary>Mốc mặc định dùng chung cho các test không quan tâm thời điểm cụ thể.</summary>
    public static DateTime Default { get; } = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    public static TestClock At(DateTime utc) => new(utc);
}
