using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Factory tạo IExchangeAccountProvider cho từng tài khoản (mỗi account có key riêng).
/// </summary>
public interface IExchangeAccountProviderFactory
{
    /// <param name="useTestnet">
    /// Phải khớp với LiveTrading.UseTestnet. Key testnet không dùng được trên endpoint thật và
    /// ngược lại, nên gọi sai sàn sẽ nhận lỗi -2015. KHÔNG đặt giá trị mặc định cho tham số này:
    /// mọi caller buộc phải nói rõ mình đang ở sàn nào.
    /// </param>
    IExchangeAccountProvider Create(string apiKey, string apiSecret, bool useTestnet);
}
