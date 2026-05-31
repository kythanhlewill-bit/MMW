using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Infrastructure.Repositories;
using MMW.Shared.Interfaces;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class CreateTradeFromSignalTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase("mmw_sig_" + Guid.NewGuid()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateFromSignal_AutoSizes_To_Risk_And_Runs_Workflow()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MmwDbContext>();

        var account = new TradingAccount
        {
            Name = "Test",
            Currency = "USDT",
            InitialBalance = 1000m,
            CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(), // maxRisk 1%, minRR 1.5
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();

        // Đề xuất Long: Entry 100, SL 97 (khoảng cách 3), TP 106 (RR 2).
        var signal = new TradeSignal
        {
            Symbol = "BTCUSDT",
            Interval = "1h",
            Direction = TradeDirection.Long,
            Bias = MarketBias.Bullish,
            Score = 2,
            Entry = 100m,
            StopLoss = 97m,
            TakeProfit = 106m,
            RiskReward = 2m,
            Reason = "test",
            CreatedAt = DateTime.UtcNow,
        };
        db.TradeSignals.Add(signal);
        await db.SaveChangesAsync();

        var tradeId = await sp.GetRequiredService<ITradeService>()
            .CreateFromSignalAsync(signal.Id, account.Id);

        var trade = await db.Trades.FindAsync(tradeId);
        Assert.NotNull(trade);

        // quantity = (1000 × 1%) / 3 = 3.33333333
        Assert.Equal(3.33333333m, trade!.Quantity);
        Assert.Equal(100m, trade.EntryPrice);
        Assert.Equal(97m, trade.StopLoss);
        Assert.Equal(TradeStatus.Open, trade.Status);

        // Auto-size đúng % rủi ro → RiskPercent ~1%, KHÔNG vi phạm RiskExceeded.
        Assert.True(trade.RiskPercent <= 1.0001m);
        var riskExceeded = db.Flags.Any(f => f.TradeId == tradeId && f.Type == FlagType.RiskExceeded);
        Assert.False(riskExceeded);
    }
}
