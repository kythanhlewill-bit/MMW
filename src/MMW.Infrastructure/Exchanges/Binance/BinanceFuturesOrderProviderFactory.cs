using Microsoft.Extensions.Options;
using MMW.Application.MarketData;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Infrastructure.Exchanges.Binance;

public class BinanceFuturesOrderProviderFactory : IExchangeOrderProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBaseRepository<ExchangeApiAuditRecord> _apiAudits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BinanceOptions _options;

    public BinanceFuturesOrderProviderFactory(
        IHttpClientFactory httpClientFactory,
        IBaseRepository<ExchangeApiAuditRecord> apiAudits,
        IUnitOfWork unitOfWork,
        IOptions<BinanceOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _apiAudits = apiAudits;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public IExchangeOrderProvider Create(string apiKey, string apiSecret, bool useTestnet)
    {
        var client = _httpClientFactory.CreateClient("BinanceFutures");
        client.BaseAddress = new Uri(useTestnet ? _options.FuturesTestnetBaseUrl : _options.FuturesApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
        return new BinanceFuturesOrderProvider(client, apiKey, apiSecret, _apiAudits, _unitOfWork);
    }
}
