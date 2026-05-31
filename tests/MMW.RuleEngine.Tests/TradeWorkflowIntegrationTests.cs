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

/// <summary>
/// Kiểm tra tích hợp toàn luồng (EF Core InMemory): tạo lệnh → Rule Engine + Behavior sinh Flag,
/// TradingDay được cập nhật.
/// </summary>
public class TradeWorkflowIntegrationTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o =>
            o.UseInMemoryDatabase("mmw_wf_" + Guid.NewGuid()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    private static Trade ClosedLoss(long accountId, DateTime opened) => new()
    {
        TradingAccountId = accountId,
        Symbol = "BTCUSDT",
        Direction = TradeDirection.Long,
        Status = TradeStatus.Closed,
        EntryPrice = 100m,
        Quantity = 1m,
        OpenedAt = opened,
        ClosedAt = opened.AddMinutes(5),
        RealizedPnl = -10m,
    };

    [Fact]
    public async Task CreateTrade_Generates_Rule_And_Behavior_Flags_And_TradingDay()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MmwDbContext>();

        // Tài khoản + cấu hình rủi ro mặc định (maxRisk 1%, minRR 1.5, requireSL, revenge 30', streak 3).
        var account = new TradingAccount
        {
            Name = "Test",
            Broker = Broker.Binance,
            Currency = "USDT",
            InitialBalance = 1000m,
            CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(),
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();

        // 3 lệnh thua liên tiếp trước đó (cùng ngày) → tạo chuỗi thua + mốc revenge.
        db.Trades.AddRange(
            ClosedLoss(account.Id, T0),
            ClosedLoss(account.Id, T0.AddHours(1)),
            ClosedLoss(account.Id, T0.AddHours(2)));
        await db.SaveChangesAsync();

        // Lệnh mới "tệ": risk 5% (>1%), R:R 0.5 (<1.5), vào 5' sau lần thua cuối.
        var dto = new TradeDto
        {
            TradingAccountId = account.Id,
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            Status = TradeStatus.Open,
            Source = TradeSource.Manual,
            EntryPrice = 100m,
            StopLoss = 90m,      // risk distance 10
            TakeProfit = 105m,   // reward distance 5 → RR 0.5
            Quantity = 5m,       // risk = 50 = 5% của 1000
            OpenedAt = T0.AddHours(2).AddMinutes(10),
        };

        var tradeService = sp.GetRequiredService<ITradeService>();
        var id = await tradeService.CreateAsync(dto);

        // --- Rule flags ---
        var ruleFlags = db.Flags.Where(f => f.TradeId == id && f.Category == FlagCategory.RuleViolation).ToList();
        Assert.Contains(ruleFlags, f => f.Type == FlagType.RiskExceeded);
        Assert.Contains(ruleFlags, f => f.Type == FlagType.LowRiskReward);

        // --- Behavior flags ---
        var behaviorFlags = db.Flags.Where(f => f.TradeId == id && f.Category == FlagCategory.Behavior).ToList();
        Assert.Contains(behaviorFlags, f => f.Type == FlagType.RevengeTrade);
        Assert.Contains(behaviorFlags, f => f.Type == FlagType.LossStreak);

        // --- Metrics đã được tính & lưu ---
        var trade = await db.Trades.FindAsync(id);
        Assert.Equal(5m, trade!.RiskPercent);
        Assert.Equal(0.5m, trade.PlannedRiskReward);

        // --- TradingDay đã được cập nhật ---
        var date = DateOnly.FromDateTime(dto.OpenedAt!.Value);
        var day = db.TradingDays.SingleOrDefault(d => d.TradingAccountId == account.Id && d.Date == date);
        Assert.NotNull(day);
        Assert.True(day!.TradeCount >= 1);
    }

    [Fact]
    public async Task GoodTrade_NoLosses_Generates_No_Flags()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MmwDbContext>();

        var account = new TradingAccount
        {
            Name = "Test2",
            Currency = "USDT",
            InitialBalance = 1000m,
            CurrentBalance = 1000m,
            RiskSetting = new RiskSetting(),
        };
        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync();

        // Lệnh "đẹp": risk 0.5% (<1%), R:R 3 (>1.5), có SL, không có lịch sử thua.
        var dto = new TradeDto
        {
            TradingAccountId = account.Id,
            Symbol = "ETHUSDT",
            Direction = TradeDirection.Long,
            Status = TradeStatus.Open,
            EntryPrice = 100m,
            StopLoss = 95m,      // risk distance 5
            TakeProfit = 115m,   // reward distance 15 → RR 3
            Quantity = 1m,       // risk = 5 = 0.5%
            OpenedAt = T0,
        };

        var id = await sp.GetRequiredService<ITradeService>().CreateAsync(dto);

        var flags = db.Flags.Where(f => f.TradeId == id).ToList();
        Assert.Empty(flags);
    }
}
