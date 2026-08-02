using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

/// <summary>
/// Cung cấp số dư dùng để TÍNH RỦI RO. Ưu tiên số dư Futures USDT THẬT trên Binance
/// (theo API key của tài khoản), fallback về CurrentBalance trong DB khi không có key/đọc lỗi.
/// Không ghi đè ledger CurrentBalance (vẫn do luồng đóng lệnh quản lý).
/// </summary>
public interface ILiveBalanceService
{
    /// <summary>Số dư hiệu lực để tính risk %. Có cache ngắn để tránh gọi sàn quá nhiều.</summary>
    Task<decimal> GetEffectiveBalanceAsync(TradingAccount account, CancellationToken cancellationToken = default);
}
