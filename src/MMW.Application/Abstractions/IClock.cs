namespace MMW.Application.Abstractions;

/// <summary>
/// Cổng thời gian — nguồn thời gian DUY NHẤT của tầng quyết định.
/// </summary>
/// <remarks>
/// Đây là hợp đồng quan trọng nhất của Deterministic Intraday Trading Engine.
/// Cùng với <c>IMarketDataProvider</c>, nó là một trong hai nguồn bất định duy nhất của engine.
/// Thay hai cổng này là chuyển được toàn bộ engine sang chế độ kiểm thử lịch sử mà
/// KHÔNG cần một dòng mã riêng nào — xem R-001.
///
/// Mọi lớp trong <c>MMW.Application.Trading</c> và <c>MMW.Application.Backtest</c> PHẢI
/// lấy thời gian qua đây. Tham chiếu trực tiếp tới <see cref="DateTime.UtcNow"/> trong hai
/// namespace đó làm ĐỎ bộ test gác hiến chương (DeterminismGuardTests) — là lỗi, không phải cảnh báo.
/// </remarks>
public interface IClock
{
    /// <summary>Thời điểm hiện tại theo UTC.</summary>
    DateTime UtcNow { get; }
}
