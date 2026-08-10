using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Infrastructure.Repositories;
using MMW.Shared.Interfaces;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// Bộ khung dựng sẵn cho các test tầng chặn theo khung giờ: một tài khoản, một
/// <see cref="EngineSetting"/> mặc định, và một cơ sở dữ liệu trong bộ nhớ riêng cho mỗi test.
/// </summary>
/// <remarks>
/// Cấu hình lấy từ <see cref="EngineSettingDefaults"/> — CHÍNH LÀ thứ seeder dùng khi chạy thật.
/// Nếu test tự dựng bảng luật riêng thì nó chứng minh cho một hệ thống không tồn tại: bảng luật
/// thật có thể sai mà mọi test vẫn xanh.
/// </remarks>
internal sealed class TimeGuardHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private TimeGuardHarness(ServiceProvider provider, long accountId, FakeMarketData marketData, TestClock clock)
    {
        _provider = provider;
        AccountId = accountId;
        MarketData = marketData;
        Clock = clock;
    }

    public long AccountId { get; }

    /// <summary>Giá và dấu thời gian sàn do test điều khiển.</summary>
    public FakeMarketData MarketData { get; }

    /// <summary>Đồng hồ do test điều khiển. Các service nhận thời điểm qua tham số vẫn ưu tiên tham số.</summary>
    public TestClock Clock { get; }

    public static async Task<TimeGuardHarness> CreateAsync(Action<EngineSetting>? configure = null)
    {
        var marketData = new FakeMarketData();
        var clock = new TestClock(TestClock.Default);

        // Tên phải tính TRƯỚC lambda: cấu hình DbContextOptions chạy lại mỗi scope, nên đặt
        // Guid.NewGuid() bên trong sẽ cho mỗi scope một cơ sở dữ liệu riêng và dữ liệu seed biến mất.
        var dbName = "mmw_timeguard_" + Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        services.AddSingleton<IMarketDataProvider>(marketData);
        services.AddSingleton<IMarketSentimentProvider>(marketData);
        services.AddSingleton<Application.Abstractions.IClock>(clock);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        // Cần một người dùng để cảnh báo lịch quá hạn có nơi mà gửi đến.
        db.Users.Add(new User { Username = "tester", DisplayName = "Tester", PasswordHash = "x", IsActive = true });

        var account = new TradingAccount
        {
            Name = "Tài khoản test",
            Currency = "USDT",
            InitialBalance = 1000m,
            CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(),
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();

        var setting = EngineSettingDefaults.Create(account.Id);
        configure?.Invoke(setting);
        db.EngineSettings.Add(setting);
        await db.SaveChangesAsync();

        return new TimeGuardHarness(provider, account.Id, marketData, clock);
    }

    /// <summary>Một scope mới = một "request", đúng như khi chạy thật.</summary>
    public IServiceScope NewScope() => _provider.CreateScope();

    public T Resolve<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>Nạp sự kiện thẳng vào lịch, mô phỏng phần nạp tay từ lịch BLS/Fed.</summary>
    public async Task AddEventsAsync(params ScheduledEvent[] events)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        db.ScheduledEvents.AddRange(events);
        await db.SaveChangesAsync();
    }

    public async Task AddClosedTradesAsync(IEnumerable<Trade> trades)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        db.Trades.AddRange(trades);
        await db.SaveChangesAsync();
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Nguồn dữ liệu thị trường giả: giá và dấu thời gian sàn do test đặt.
/// </summary>
/// <remarks>
/// Năm phương thức futures trả <c>null</c> theo đúng hợp đồng lỗi của
/// <see cref="IMarketDataProvider"/> — thiếu dữ liệu là trạng thái hợp lệ, không phải ngoại lệ.
/// </remarks>
internal sealed class FakeMarketData : IMarketDataProvider, IMarketSentimentProvider
{
    /// <summary>Giá theo mã. Không có mã nào khớp thì <see cref="GetTickerAsync"/> ném.</summary>
    public Dictionary<string, decimal> Prices { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nến theo mã. Mặc định rỗng — nguồn thiếu dữ liệu là trạng thái hợp lệ.</summary>
    public Dictionary<string, IReadOnlyList<Candle>> Candles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<FundingRatePoint>> FundingHistory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dấu thời gian của sàn, dùng để thử phát hiện lệch đồng hồ. Null = sàn không trả.</summary>
    public DateTime? ExchangeTimeUtc { get; set; }

    public decimal? FundingRate { get; set; }
    public OpenInterestSeries? OpenInterest { get; set; }
    public LongShortRatio? LongShort { get; set; }
    public int? FearGreed { get; set; }

    /// <summary>Bật để mô phỏng nguồn giá lỗi mạng — nguồn nến ném thay vì trả rỗng.</summary>
    public bool ThrowOnCandles { get; set; }

    public Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default) =>
        Prices.TryGetValue(symbol, out var price)
            ? Task.FromResult(new Ticker(symbol, price))
            : throw new InvalidOperationException($"Test chưa đặt giá cho {symbol}.");

    public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default) =>
        ThrowOnCandles
            ? throw new HttpRequestException($"Giả lập lỗi mạng khi lấy nến {symbol}.")
            : Task.FromResult(Candles.TryGetValue(symbol, out var c) ? c : Array.Empty<Candle>());

    public Task<IReadOnlyList<Candle>> GetCandleHistoryAsync(
        string symbol, string interval, DateTime startTimeUtc, int limit = 1000, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Candle>>(Candles.TryGetValue(symbol, out var candles)
            ? candles.Where(x => x.OpenTime >= startTimeUtc).OrderBy(x => x.OpenTime).Take(limit).ToList()
            : Array.Empty<Candle>());

    public Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<SymbolPriceFilter?>(null);

    public Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExchangeTimeUtc is null && FundingRate is null
            ? null
            : new FundingSnapshot(
                FundingRate ?? 0m,
                (ExchangeTimeUtc ?? DateTime.UnixEpoch).AddHours(1),
                100m,
                ExchangeTimeUtc ?? DateTime.UnixEpoch));

    public Task<IReadOnlyList<FundingRatePoint>?> GetFundingHistoryAsync(
        string symbol, DateTime startTimeUtc, int limit = 500, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FundingRatePoint>?>(FundingHistory.TryGetValue(symbol, out var points)
            ? points.Where(x => x.FundingTimeUtc >= startTimeUtc).OrderBy(x => x.FundingTimeUtc).Take(limit).ToList()
            : null);

    public Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenInterest);

    public Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) =>
        Task.FromResult(LongShort);

    public Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<DepthSnapshot?>(null);

    public Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<TakerFlow?>(null);

    public Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default) => Task.FromResult(FearGreed);
}
