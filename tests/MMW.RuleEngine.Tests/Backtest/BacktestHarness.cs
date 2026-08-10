using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application;
using MMW.Application.Abstractions;
using MMW.Application.Backtest;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Infrastructure.Repositories;
using MMW.RuleEngine.Tests.Scoring;
using MMW.Shared.Interfaces;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// Bộ khung chạy engine ở CHẾ ĐỘ KIỂM THỬ LỊCH SỬ: đúng hai cổng bị thay, mọi thứ khác giữ
/// nguyên như chạy thật.
/// </summary>
/// <remarks>
/// Việc bộ khung này chỉ thay được hai dòng đăng ký chính là bằng chứng cho FR-053. Nếu một
/// ngày nào đó phải thay thêm thứ gì nữa để kiểm thử chạy được, thì đã có một nhánh mã riêng
/// lọt vào tầng quyết định.
/// </remarks>
internal sealed class BacktestHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private BacktestHarness(ServiceProvider provider, long accountId, BacktestClock clock)
    {
        _provider = provider;
        AccountId = accountId;
        Clock = clock;
    }

    public long AccountId { get; }
    public BacktestClock Clock { get; }

    public static async Task<BacktestHarness> CreateAsync(
        DateTime startUtc, IReadOnlyList<Candle> candles, string symbol = "BTCUSDT")
    {
        var dbName = "mmw_backtest_" + Guid.NewGuid();
        var clock = new BacktestClock(startUtc);

        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();

        // ── Hai cổng bị thay, và CHỈ hai cổng này ───────────────────────
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(clock);
        services.AddScoped<IKlineArchiveReader, KlineArchiveReader>();
        services.AddScoped<IMarketDataProvider, ArchiveMarketDataProvider>();

        // Nguồn tâm lý thị trường không có lịch sử — trả null, đúng như R-003.
        services.AddSingleton<IMarketSentimentProvider>(new NullSentiment());
        services.AddScoped<IBacktestEngine, BacktestEngine>();

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        db.Users.Add(new User { Username = "tester", DisplayName = "Tester", PasswordHash = "x", IsActive = true });

        var account = new TradingAccount
        {
            Name = "Backtest", Currency = "USDT",
            InitialBalance = 1000m, CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(),
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();

        var setting = EngineSettingDefaults.Create(account.Id);
        setting.Symbols = symbol;
        db.EngineSettings.Add(setting);

        foreach (var c in candles)
        {
            db.KlineArchives.Add(new KlineArchive
            {
                Symbol = symbol, Interval = "15m",
                OpenTimeUtc = c.OpenTime, CloseTimeUtc = c.CloseTime,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
            });
        }
        await db.SaveChangesAsync();

        return new BacktestHarness(provider, account.Id, clock);
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    public void Dispose() => _provider.Dispose();

    /// <summary>Chuỗi nến 15m liên tục, có điểm xoay thật để tầng cấu trúc chấm được.</summary>
    public static List<Candle> Series(DateTime startUtc, int count)
    {
        var shape = new[] { 0m, 0.25m, 0.5m, 0.75m, 1m, 0.75m, 0.5m, 0.25m };

        return Enumerable.Range(0, count).Select(i =>
        {
            var open = startUtc.AddMinutes(15 * i);
            var price = 1000m + i / shape.Length * 4m + shape[i % shape.Length] * 20m;
            return new Candle(open, price, price + 1m, price - 1m, price, 100m,
                open.AddMinutes(15).AddTicks(-1));
        }).ToList();
    }

    private sealed class NullSentiment : IMarketSentimentProvider
    {
        public Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default) => Task.FromResult<int?>(null);
    }
}
