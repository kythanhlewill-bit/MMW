using Microsoft.Extensions.Options;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;

namespace MMW.Infrastructure.Exchanges.Binance;

public class BinanceAccountProviderFactory : IExchangeAccountProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BinanceOptions _options;

    public BinanceAccountProviderFactory(IHttpClientFactory httpClientFactory, IOptions<BinanceOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public IExchangeAccountProvider Create(string apiKey, string apiSecret, bool useTestnet)
    {
        var client = _httpClientFactory.CreateClient("BinanceApi");
        client.BaseAddress = new Uri(_options.ApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);

        // Cả hai endpoint mà provider này dùng (/fapi/v2/balance, /fapi/v1/userTrades) đều là
        // Futures, nên khi chạy testnet phải trỏ sang testnet.binancefuture.com — giống hệt điều
        // BinanceFuturesOrderProviderFactory đã làm cho phía đặt lệnh.
        var futuresBase = useTestnet ? _options.FuturesTestnetBaseUrl : _options.FuturesApiBaseUrl;

        var opts = Options.Create(new BinanceOptions
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            ApiBaseUrl = _options.ApiBaseUrl,
            MarketDataBaseUrl = _options.MarketDataBaseUrl,
            FuturesApiBaseUrl = futuresBase,
        });

        return new BinanceAccountProvider(client, opts);
    }
}
