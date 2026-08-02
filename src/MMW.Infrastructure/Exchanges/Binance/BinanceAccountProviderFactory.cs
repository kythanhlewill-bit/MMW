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

    public IExchangeAccountProvider Create(string apiKey, string apiSecret)
    {
        var client = _httpClientFactory.CreateClient("BinanceApi");
        client.BaseAddress = new Uri(_options.ApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);

        var opts = Options.Create(new BinanceOptions
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            ApiBaseUrl = _options.ApiBaseUrl,
            MarketDataBaseUrl = _options.MarketDataBaseUrl,
            FuturesApiBaseUrl = _options.FuturesApiBaseUrl,
        });

        return new BinanceAccountProvider(client, opts);
    }
}
