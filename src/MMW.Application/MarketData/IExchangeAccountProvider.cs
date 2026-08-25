using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Cổng đọc dữ liệu tài khoản trên sàn (cần API key READ-ONLY). Không bao giờ đặt/huỷ lệnh.
/// </summary>
public interface IExchangeAccountProvider
{
    Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Số dư của MỘT tài sản trong ví USD-M Futures. Trả null nếu sàn không liệt kê tài sản đó.
    /// </summary>
    /// <param name="quoteAsset">
    /// Tài sản định giá của cặp sắp vào lệnh — cũng chính là ví trả ký quỹ cho nó. Ở chế độ ký
    /// quỹ đơn tài sản, ví USDT và ví USDC là hai túi riêng: hỏi sai túi thì con số trả về đúng
    /// về mặt kỹ thuật nhưng vô nghĩa với lệnh đang định vào.
    /// </param>
    Task<decimal?> GetFuturesBalanceAsync(string quoteAsset, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeTrade>> GetMyTradesAsync(string symbol, int limit = 500, CancellationToken cancellationToken = default);
}
