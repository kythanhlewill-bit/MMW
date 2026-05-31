using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

public interface ITradingDayService
{
    /// <summary>
    /// Tính lại tổng hợp ngày (số lệnh, win/loss, PnL, chuỗi thua...) từ các lệnh đã vào
    /// và upsert vào bảng TradingDays. Idempotent.
    /// </summary>
    Task<TradingDay> RecomputeAndSaveAsync(long accountId, DateOnly date, CancellationToken cancellationToken = default);
}
