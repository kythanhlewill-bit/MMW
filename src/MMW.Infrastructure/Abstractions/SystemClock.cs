using MMW.Application.Abstractions;

namespace MMW.Infrastructure.Abstractions;

/// <summary>Đồng hồ thật, dùng khi chạy live. Bản kiểm thử lịch sử là <c>BacktestClock</c>.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
