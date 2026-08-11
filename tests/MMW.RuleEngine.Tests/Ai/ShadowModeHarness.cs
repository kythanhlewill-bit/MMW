using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Application.Services;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Infrastructure.Repositories;
using MMW.Shared.Interfaces;

namespace MMW.RuleEngine.Tests.Ai;

internal sealed class ShadowModeHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private ShadowModeHarness(ServiceProvider provider, FakeLlm llm, List<string> logs)
    {
        _provider = provider;
        Llm = llm;
        Logs = logs;
    }

    public FakeLlm Llm { get; }
    public List<string> Logs { get; }

    public static async Task<ShadowModeHarness> CreateAsync(bool shadowEnabled)
    {
        var llm = new FakeLlm
        {
            DefaultResponse = """
                {"action":"long","score":5,"confidence":0.8,"entry":100,
                 "stopLoss":98,"takeProfit":104,"riskReward":2,"reason":"setup mạnh",
                 "invalidation":"thủng hỗ trợ","warnings":[]}
                """,
        };

        var services = new ServiceCollection();
        var logs = new List<string>();
        var databaseName = "mmw_shadow_" + Guid.NewGuid();
        services.AddDbContext<MmwDbContext>(o =>
            o.UseInMemoryDatabase(databaseName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IMarketScanService, MarketScanService>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<MarketScanService>>(new CaptureLogger<MarketScanService>(logs));
        services.AddSingleton<ILlmService>(llm);
        services.AddSingleton<IMarketDataProvider, ShadowMarketData>();
        services.AddSingleton<IMarketAnalyzer, ShadowAnalyzer>();
        services.AddSingleton<ITradePreflightAnalysisService, AcceptingPreflight>();
        services.AddSingleton<IMacroEventService, EmptyMacroEvents>();
        services.AddSingleton<INotificationService, SilentNotifications>();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        db.AppSettings.Add(new AppSetting
        {
            ShadowComparisonEnabled = shadowEnabled,
            AutoCreateTradeFromSignal = true,
            MinSignalScore = 3,
        });
        db.WatchItems.Add(new WatchItem { Symbol = "BTCUSDT", Interval = "15m", IsActive = true });
        await db.SaveChangesAsync();

        return new ShadowModeHarness(provider, llm, logs);
    }

    public async Task ScanAsync()
    {
        using var scope = _provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMarketScanService>().ScanAllAsync();
        if (result.Failed > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, Logs));
    }

    public async Task AddDeterministicScorecardAsync(ScorecardOutcome outcome, TradeDirection direction)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        db.EntryScorecards.Add(new EntryScorecard
        {
            TradingAccountId = 1,
            Symbol = "BTCUSDT",
            Interval = "15m",
            CandleCloseTimeUtc = DateTime.UtcNow.AddMinutes(-1),
            EvaluatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Direction = direction,
            TotalScore = outcome == ScorecardOutcome.Entered ? 80 : 40,
            Outcome = outcome,
            InputSnapshotJson = "{}",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Lùi mốc quét của mọi audit AI về quá khứ — giả lập "đã sang cây nến mới".
    /// </summary>
    public async Task BackdateAiAuditsAsync(TimeSpan by)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        foreach (var audit in await db.AiSignalScanRecords.ToListAsync())
            audit.ScannedAt -= by;
        await db.SaveChangesAsync();
    }

    public async Task<List<T>> ReadAsync<T>() where T : class
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MmwDbContext>()
            .Set<T>().AsNoTracking().ToListAsync();
    }

    public void Dispose() => _provider.Dispose();

    private sealed class ShadowMarketData : IMarketDataProvider
    {
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            IReadOnlyList<Candle> candles =
            [new Candle(now.AddMinutes(-30), 99m, 101m, 98m, 100m, 1000m, now.AddMinutes(-15))];
            return Task.FromResult(candles);
        }

        public Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Ticker(symbol, 100m));
        public Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromResult<SymbolPriceFilter?>(null);
        public Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromResult<FundingSnapshot?>(null);
        public Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken cancellationToken = default) => Task.FromResult<OpenInterestSeries?>(null);
        public Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) => Task.FromResult<LongShortRatio?>(null);
        public Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<DepthSnapshot?>(null);
        public Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken cancellationToken = default) => Task.FromResult<TakerFlow?>(null);
    }

    private sealed class ShadowAnalyzer : IMarketAnalyzer
    {
        public MarketAnalysis Analyze(IReadOnlyList<Candle> candles, decimal currentPrice) =>
            new(100m, 55m, 99m, 98m, 1m, 0.5m, 0.5m, 2m, MarketBias.Bullish, 5, "test");
    }

    private sealed class AcceptingPreflight : ITradePreflightAnalysisService
    {
        public Task<TradePreflightAnalysisResult> AnalyzeAsync(TradePreflightAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TradePreflightAnalysisResult
            {
                IsAiConfigured = true,
                AiAnswered = true,
                Decision = "accept",
                Score = 5,
                Confidence = 0.8m,
                Advice = "chỉ dùng để so sánh",
            });
    }

    private sealed class EmptyMacroEvents : IMacroEventService
    {
        public Task<MacroEventContext> GetContextForTradeAsync(string symbol, DateTime utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MacroEventContext());
        public Task<int> ScanAndNotifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class SilentNotifications : INotificationService
    {
        public Task<IReadOnlyList<NotificationModel>> PublishAsync(NotificationCreateModel model, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationModel>>([]);
        public Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationModel>>([]);
        public Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationModel>>([]);
        public Task<int> GetUnreadCountAsync(long userId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkAllAsReadAsync(long userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CaptureLogger<T>(List<string> messages) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception) + (exception is null ? "" : $" | {exception}"));
    }
}
