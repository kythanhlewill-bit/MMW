using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Cổng đọc dữ liệu tài khoản trên sàn (cần API key READ-ONLY). Không bao giờ đặt/huỷ lệnh.
/// </summary>
public interface IExchangeAccountProvider
{
    Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lấy số dư USDT thực tế từ tài khoản USD-M Futures. Trả null nếu không tìm thấy.</summary>
    Task<decimal?> GetFuturesUsdtBalanceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeTrade>> GetMyTradesAsync(string symbol, int limit = 500, CancellationToken cancellationToken = default);
}
