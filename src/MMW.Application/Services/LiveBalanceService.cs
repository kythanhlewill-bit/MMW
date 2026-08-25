using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Constants;
using MMW.Domain.Entities;

namespace MMW.Application.Services;

/// <summary>
/// Lấy số dư Futures thật của đúng ví trả ký quỹ cho cặp đang xét (có cache 30s theo tài khoản + ví),
/// fallback CurrentBalance khi không có key hoặc gọi sàn lỗi.
/// </summary>
public class LiveBalanceService : ILiveBalanceService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IExchangeAccountProviderFactory _accountFactory;
    private readonly IMemoryCache _cache;
    private readonly LiveTradingOptions _liveTrading;

    public LiveBalanceService(
        IExchangeAccountProviderFactory accountFactory,
        IMemoryCache cache,
        IOptions<LiveTradingOptions> liveTrading)
    {
        _accountFactory = accountFactory;
        _cache = cache;
        _liveTrading = liveTrading.Value;
    }

    public async Task<decimal> GetEffectiveBalanceAsync(
        TradingAccount account, string? quoteAsset = null, CancellationToken cancellationToken = default)
    {
        if (account is null) return 0m;

        var asset = string.IsNullOrWhiteSpace(quoteAsset)
            ? SymbolConventions.DefaultQuoteAsset
            : quoteAsset.Trim().ToUpperInvariant();

        var fallback = account.CurrentBalance;
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
            return fallback;

        // Khoá cache phải mang cả tên ví. Thiếu nó thì lần đọc đầu tiên — bất kể ví nào — sẽ phục vụ
        // cả hai ví trong 30 giây tiếp theo, và đó đúng là kiểu sai không bao giờ nổ ra lỗi:
        // con số trả về luôn hợp lệ, chỉ là của túi tiền khác.
        var cacheKey = $"livebal:{account.Id}:{asset}";
        if (account.Id != 0 && _cache.TryGetValue(cacheKey, out decimal cached))
            return cached;

        try
        {
            var provider = _accountFactory.Create(account.ApiKey!, account.ApiSecret!, _liveTrading.UseTestnet);
            var real = await provider.GetFuturesBalanceAsync(asset, cancellationToken);
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
