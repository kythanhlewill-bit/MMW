using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

/// <summary>
/// Cung cấp số dư dùng để TÍNH RỦI RO. Ưu tiên số dư Futures THẬT trên Binance của đúng ví trả
/// ký quỹ cho cặp đang xét (theo API key của tài khoản), fallback về CurrentBalance trong DB khi
/// không có key/đọc lỗi. Không ghi đè ledger CurrentBalance (vẫn do luồng đóng lệnh quản lý).
/// </summary>
public interface ILiveBalanceService
{
    /// <summary>Số dư hiệu lực để tính risk %. Có cache ngắn để tránh gọi sàn quá nhiều.</summary>
    /// <param name="quoteAsset">
    /// Ví trả ký quỹ, thường lấy từ <c>SymbolConventions.QuoteAssetOf(symbol)</c>. Bỏ trống thì
    /// rơi về USDT — giữ nguyên hành vi cũ cho các màn hình tổng quan chưa gắn với một cặp nào.
    /// </param>
    Task<decimal> GetEffectiveBalanceAsync(
        TradingAccount account, string? quoteAsset = null, CancellationToken cancellationToken = default);
}
