using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Infrastructure.Repositories;
using MMW.Shared.Interfaces;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class CloseTradeTests
{
    // DB name ổn định để mọi scope dùng chung dữ liệu; mỗi scope = 1 DbContext (mô phỏng 1 request).
    private static ServiceProvider BuildProvider()
    {
        var dbName = "mmw_close_" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    private static async Task<long> SeedAccountAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var account = new TradingAccount
        {
            Name = "T", Currency = "USDT",
            InitialBalance = 1000m, CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(),
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<long> CreateAsync(ServiceProvider provider, TradeDto dto)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITradeService>().CreateAsync(dto);
    }

    [Fact]
    public async Task Close_Long_Win_Computes_Pnl_Updates_Balance_And_Outcome()
    {
        using var provider = BuildProvider();
        var accountId = await SeedAccountAsync(provider);

        var id = await CreateAsync(provider, new TradeDto
        {
            TradingAccountId = accountId,
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            Status = TradeStatus.Open,
            EntryPrice = 100m,
            StopLoss = 95m,      // risk distance 5
            Quantity = 2m,       // riskAmount = 10
            OpenedAt = DateTime.UtcNow,
        });

        // Đóng tại 110 → PnL = (110-100)*2 = 20 (scope/request riêng).
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITradeService>()
                .CloseAsync(id, exitPrice: 110m, emotionAfter: EmotionState.Disciplined);
        }

        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var trade = await db.Trades.FindAsync(id);
        Assert.Equal(TradeStatus.Closed, trade!.Status);
        Assert.Equal(20m, trade.RealizedPnl);
        Assert.Equal(TradeOutcome.Win, trade.Outcome);
        Assert.Equal(2m, trade.RMultiple);          // 20 / 10
        Assert.Equal(110m, trade.ExitPrice);
        Assert.NotNull(trade.ClosedAt);

        var acc = await db.TradingAccounts.FindAsync(accountId);
        Assert.Equal(1020m, acc!.CurrentBalance);   // 1000 + 20
    }

    [Fact]
    public async Task Close_Short_Loss_Subtracts_From_Balance()
    {
        using var provider = BuildProvider();
        var accountId = await SeedAccountAsync(provider);

        var id = await CreateAsync(provider, new TradeDto
        {
            TradingAccountId = accountId,
            Symbol = "ETHUSDT",
            Direction = TradeDirection.Short,
            Status = TradeStatus.Open,
            EntryPrice = 100m,
            StopLoss = 105m,
            Quantity = 3m,
            OpenedAt = DateTime.UtcNow,
        });

        // Short, đóng tại 104 → PnL = (100-104)*3 = -12.
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITradeService>()
                .CloseAsync(id, exitPrice: 104m, emotionAfter: EmotionState.Tilted);
        }

        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var trade = await db.Trades.FindAsync(id);
        Assert.Equal(-12m, trade!.RealizedPnl);
        Assert.Equal(TradeOutcome.Loss, trade.Outcome);

        var acc = await db.TradingAccounts.FindAsync(accountId);
        Assert.Equal(988m, acc!.CurrentBalance);    // 1000 - 12
    }
}
