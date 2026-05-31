using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Factory tạo IExchangeAccountProvider cho từng tài khoản (mỗi account có key riêng).
/// </summary>
public interface IExchangeAccountProviderFactory
{
    IExchangeAccountProvider Create(string apiKey, string apiSecret);
}
