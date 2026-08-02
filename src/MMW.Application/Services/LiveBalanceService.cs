using Microsoft.Extensions.Caching.Memory;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Entities;

namespace MMW.Application.Services;

/// <summary>
/// Lấy số dư Futures USDT thật từ Binance theo key của từng tài khoản (có cache 30s),
/// fallback CurrentBalance khi không có key hoặc gọi sàn lỗi.
/// </summary>
public class LiveBalanceService : ILiveBalanceService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IExchangeAccountProviderFactory _accountFactory;
    private readonly IMemoryCache _cache;

    public LiveBalanceService(IExchangeAccountProviderFactory accountFactory, IMemoryCache cache)
    {
        _accountFactory = accountFactory;
        _cache = cache;
    }

    public async Task<decimal> GetEffectiveBalanceAsync(TradingAccount account, CancellationToken cancellationToken = default)
    {
        if (account is null) return 0m;

        var fallback = account.CurrentBalance;
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
            return fallback;

        var cacheKey = $"livebal:{account.Id}";
        if (account.Id != 0 && _cache.TryGetValue(cacheKey, out decimal cached))
            return cached;

        try
        {
            var provider = _accountFactory.Create(account.ApiKey!, account.ApiSecret!);
            var real = await provider.GetFuturesUsdtBalanceAsync(cancellationToken);
            var value = real is > 0m ? real.Value : fallback;
            if (account.Id != 0)
                _cache.Set(cacheKey, value, CacheTtl);
            return value;
        }
        catch
        {
            return fallback; // không đọc được sàn → dùng số dư DB
        }
    }
}
