namespace MMW.Application.MarketData;

/// <summary>
/// Tạo IExchangeOrderProvider cho từng tài khoản (mỗi account có API key riêng).
/// useTestnet=true → trỏ sang Binance Futures Testnet (tiền ảo).
/// </summary>
public interface IExchangeOrderProviderFactory
{
    IExchangeOrderProvider Create(string apiKey, string apiSecret, bool useTestnet);
}
