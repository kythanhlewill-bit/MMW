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

public class CrudTradeTests
{
    private static ServiceProvider BuildProvider()
    {
        var dbName = "mmw_crud_" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    private static async Task<long> SeedAccountAsync(ServiceProvider p)
    {
        using var scope = p.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var acc = new TradingAccount { Name = "T", Currency = "USDT", InitialBalance = 1000m, CurrentBalance = 1000m, RiskSetting = new RiskSetting() };
        db.TradingAccounts.Add(acc);
        await db.SaveChangesAsync();
        return acc.Id;
    }

    private static async Task<long> CreateAsync(ServiceProvider p, TradeDto dto)
    {
        using var scope = p.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITradeService>().CreateAsync(dto);
    }

    [Fact]
    public async Task Update_Recomputes_Metrics_And_Flags()
    {
        using var provider = BuildProvider();
        var accId = await SeedAccountAsync(provider);

        // Lệnh ban đầu tuân thủ: risk 1% (qty 2, dist 5, equity 1000).
        var id = await CreateAsync(provider, new TradeDto
        {
            TradingAccountId = accId, Symbol = "BTCUSDT", Direction = TradeDirection.Long,
            Status = TradeStatus.Open, EntryPrice = 100m, StopLoss = 95m, Quantity = 2m, OpenedAt = DateTime.UtcNow,
        });

        // Sửa tăng khối lượng lên 6 → risk = 30 = 3% > 1% → phải sinh RiskExceeded.
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITradeService>().UpdateAsync(id, new TradeDto
            {
                TradingAccountId = accId, Symbol = "BTCUSDT", Direction = TradeDirection.Long,
                Status = TradeStatus.Open, EntryPrice = 100m, StopLoss = 95m, Quantity = 6m,
            });
        }

        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var trade = await db.Trades.FindAsync(id);
        Assert.Equal(6m, trade!.Quantity);
        Assert.Equal(3m, trade.RiskPercent);
        Assert.True(db.Flags.Any(f => f.TradeId == id && f.Type == FlagType.RiskExceeded));
    }

    [Fact]
    public async Task Delete_Closed_Trade_Restores_Balance_And_Removes_Flags()
    {
        using var provider = BuildProvider();
        var accId = await SeedAccountAsync(provider);

        var id = await CreateAsync(provider, new TradeDto
        {
            TradingAccountId = accId, Symbol = "BTCUSDT", Direction = TradeDirection.Long,
            Status = TradeStatus.Open, EntryPrice = 100m, StopLoss = 95m, Quantity = 2m, OpenedAt = DateTime.UtcNow,
        });

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ITradeService>().CloseAsync(id, 110m, EmotionState.Calm);

        // Sau close: balance = 1000 + 20 = 1020.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
            Assert.Equal(1020m, (await db.TradingAccounts.FindAsync(accId))!.CurrentBalance);
        }

        // Xoá → hoàn 20 → balance về 1000, lệnh & flag biến mất.
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ITradeService>().DeleteAsync(id);

        using var verify = provider.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Null(await vdb.Trades.FindAsync(id));
        Assert.Equal(1000m, (await vdb.TradingAccounts.FindAsync(accId))!.CurrentBalance);
        Assert.False(vdb.Flags.Any(f => f.TradeId == id));
    }
}
