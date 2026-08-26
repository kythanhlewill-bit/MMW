using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using Xunit;

namespace MMW.RuleEngine.Tests;

public class LiveOrderTests
{
    // --- Fakes ---
    private sealed class FakeOrderProvider : IExchangeOrderProvider
    {
        public readonly List<FuturesOrderRequest> Placed = new();
        public int LeverageCalls;
        public int CancelAllCalls;
        public int CloseCalls;
        public IReadOnlyList<ExchangePosition> Positions = new List<ExchangePosition>();

        /// <summary>Phần thân phản hồi mà sàn trả về thay cho lệnh đầu tiên, hoặc null nếu nhận.</summary>
        public string? RefuseFirstPlacementWith;

        public Task<string> ValidateFuturesOrderAsync(FuturesOrderRequest req, CancellationToken ct = default)
            => Task.FromResult("OK");

        public Task<ExchangeOrderResult> PlaceFuturesOrderAsync(FuturesOrderRequest req, CancellationToken ct = default)
        {
            if (RefuseFirstPlacementWith is { } body)
            {
                RefuseFirstPlacementWith = null;
                // Đúng dạng mà BinanceFuturesOrderProvider ném: mã lỗi nằm trong Message.
                throw new InvalidOperationException($"Binance order lỗi (400): {body}");
            }

            Placed.Add(req);
            return Task.FromResult(new ExchangeOrderResult("999", req.NewClientOrderId, "NEW"));
        }
        public Task SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
        {
            LeverageCalls++;
            return Task.CompletedTask;
        }
        public Task<decimal> NormalizeQuantityAsync(string symbol, decimal desiredQty, CancellationToken ct = default)
            => Task.FromResult(desiredQty); // test: không có sàn thật → giữ nguyên
        public Task<decimal> NormalizeQuantityForNotionalAsync(string symbol, decimal desiredQty, decimal entryPrice, decimal minNotionalUsdt, CancellationToken ct = default)
            => Task.FromResult(entryPrice > 0m && desiredQty * entryPrice < minNotionalUsdt
                ? minNotionalUsdt / entryPrice
                : desiredQty);
        public Task CancelOrderAsync(string symbol, string orderId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(string? symbol = null, CancellationToken ct = default)
            => Task.FromResult(Positions);
        public Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ExchangeOpenOrder>)new List<ExchangeOpenOrder>());
        public Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenConditionalOrdersAsync(string? symbol = null, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ExchangeOpenOrder>)new List<ExchangeOpenOrder>());
        public Task CancelAllOpenOrdersAsync(string symbol, CancellationToken ct = default) { CancelAllCalls++; return Task.CompletedTask; }
        public Task ClosePositionAsync(string symbol, CancellationToken ct = default) { CloseCalls++; return Task.CompletedTask; }
    }

    private sealed class FakeSettings : ISettingsService
    {
        private readonly bool _override;
        public FakeSettings(bool allowOverride) => _override = allowOverride;
        public Task<AppSetting> GetAppSettingAsync(CancellationToken ct = default)
            => Task.FromResult(new AppSetting { AllowOverrideRisk = _override });
        public Task UpdateAppSettingAsync(long? a, bool b, int c, bool d, bool e, bool f, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RiskSetting> GetRiskSettingAsync(long accountId, CancellationToken ct = default) => Task.FromResult(new RiskSetting());
        public Task UpsertRiskSettingAsync(long accountId, RiskSetting values, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeWorkflow : ITradeWorkflowService
    {
        public Task<TradeAnalysisResult> ProcessTradeAsync(long tradeId, CancellationToken ct = default)
            => Task.FromResult(new TradeAnalysisResult(new List<Flag>(), new List<Flag>()));
    }

    private sealed class FakeOrderFactory : IExchangeOrderProviderFactory
    {
        public readonly FakeOrderProvider Provider = new();
        public IExchangeOrderProvider Create(string apiKey, string apiSecret, bool useTestnet) => Provider;
    }

    private sealed class FakeLlm : ILlmService
    {
        public FakeLlm(bool configured) => IsConfigured = configured;
        public bool IsConfigured { get; }
        public Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeNotifications : INotificationService
    {
        public Task<IReadOnlyList<NotificationModel>> PublishAsync(NotificationCreateModel m, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<NotificationModel>)new List<NotificationModel>());
        public Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long u, int take = 20, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<NotificationModel>)new List<NotificationModel>());
        public Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long u, int skip, int take, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<NotificationModel>)new List<NotificationModel>());
        public Task<int> GetUnreadCountAsync(long u, CancellationToken ct = default) => Task.FromResult(0);
        public Task MarkAsReadAsync(long u, long n, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkAllAsReadAsync(long u, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider()
    {
        var dbName = "mmw_live_" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services.BuildServiceProvider();
    }

    private static LiveOrderService BuildService(IServiceScope scope, IExchangeOrderProviderFactory factory, LiveTradingOptions opts, bool aiConfigured = true, bool allowOverrideRisk = false) =>
        new(
            scope.ServiceProvider.GetRequiredService<IBaseRepository<Trade>>(),
            scope.ServiceProvider.GetRequiredService<IBaseRepository<TradingAccount>>(),
            scope.ServiceProvider.GetRequiredService<IBaseRepository<Flag>>(),
            factory,
            new FakeNotifications(),
            new FakeLlm(aiConfigured),
            new FakeWorkflow(),
            new FakeSettings(allowOverrideRisk),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            Options.Create(opts),
            NullLogger<LiveOrderService>.Instance);

    private static async Task<(long accId, long tradeId)> SeedAsync(ServiceProvider p, bool withCritical = false, decimal qty = 0.1m, decimal lev = 5m,
        TradeDirection direction = TradeDirection.Long, OrderType orderType = OrderType.Market,
        decimal entry = 100m, decimal? stopLoss = 95m, decimal? takeProfit = 110m)
    {
        using var scope = p.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var acc = new TradingAccount
        {
            Name = "Live", Currency = "USDT", InitialBalance = 1000m, CurrentBalance = 1000m,
            ApiKey = "k", ApiSecret = "s", RiskSetting = new RiskSetting()
        };
        db.TradingAccounts.Add(acc);
        await db.SaveChangesAsync();

        var trade = new Trade
        {
            TradingAccountId = acc.Id, Symbol = "BTCUSDT", Direction = direction,
            Status = TradeStatus.Open, OrderType = orderType,
            EntryPrice = entry, StopLoss = stopLoss, TakeProfit = takeProfit, Quantity = qty, Leverage = lev,
            OpenedAt = DateTime.UtcNow,
        };
        db.Trades.Add(trade);
        await db.SaveChangesAsync();

        if (withCritical)
        {
            db.Flags.Add(new Flag
            {
                TradingAccountId = acc.Id, TradeId = trade.Id, Category = FlagCategory.RuleViolation,
                Type = FlagType.RiskExceeded, Severity = FlagSeverity.Critical, Message = "Risk quá cao",
            });
            await db.SaveChangesAsync();
        }
        return (acc.Id, trade.Id);
    }

    private static LiveTradingOptions Opts(bool enabled = true) => new()
    {
        Enabled = enabled, UseTestnet = true, MaxLeverage = 20, MaxNotionalUsdt = 100000m, MaxOrdersPerDay = 100,
    };

    [Fact]
    public async Task Places_Entry_Sl_Tp_When_All_Clear()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Equal(3, factory.Provider.Placed.Count); // entry + SL + TP
        Assert.Equal(1, factory.Provider.LeverageCalls);

        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.True(t!.IsLive);
        Assert.Equal(LiveOrderStatus.Submitted, t.LiveStatus);
    }

    /// <summary>
    /// Sàn từ chối lệnh chờ (post-only) là kết cục THỊ TRƯỜNG, không phải lỗi kỹ thuật.
    /// </summary>
    /// <remarks>
    /// Hai lệnh ETHUSDT #37 và #50 (25–26/08/2026) bị Binance trả -5022 và bị hệ ghi thành
    /// <c>Error</c> kèm nguyên khối JSON của sàn trong <c>LiveNote</c>. Hai cái sai trong một:
    /// nó thổi phồng số lỗi kỹ thuật, và nó xoá mất tín hiệu duy nhất cho biết ngưỡng khoảng cách
    /// tối thiểu của mức chờ đang đặt quá mỏng.
    /// </remarks>
    [Fact]
    public async Task Post_only_bi_tu_choi_thi_khong_tinh_la_loi()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();
        factory.Provider.RefuseFirstPlacementWith =
            "{\"code\":-5022,\"msg\":\"Due to the order could not be executed as maker, "
            + "the Post Only order will be rejected.\"}";

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        // Entry bị từ chối ⟹ không có vị thế ⟹ tuyệt đối không được đặt SL/TP lên hư không.
        Assert.Empty(factory.Provider.Placed);

        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);

        Assert.Equal(LiveOrderStatus.PostOnlyRejected, t!.LiveStatus);
        Assert.NotEqual(LiveOrderStatus.Error, t.LiveStatus);
        Assert.Equal(TradeStatus.Cancelled, t.Status);

        // Lý do phải đọc được, không phải JSON của sàn dán nguyên vào nhật ký.
        Assert.DoesNotContain("-5022", t.LiveNote!);
        Assert.Contains("không còn thụ động", t.LiveNote!);
    }

    /// <summary>Lỗi sàn THẬT vẫn phải vào cột lỗi — nhánh mới không được nuốt hết mọi thứ.</summary>
    [Fact]
    public async Task Loi_san_that_van_bi_ghi_la_loi()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();
        factory.Provider.RefuseFirstPlacementWith = "{\"code\":-2019,\"msg\":\"Margin is insufficient.\"}";

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);

        Assert.Equal(LiveOrderStatus.Error, t!.LiveStatus);
        Assert.Equal(TradeStatus.Cancelled, t.Status);
    }

    [Fact]
    public async Task Blocks_When_Critical_Flag()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, withCritical: true);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed); // không gửi gì
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.False(t!.IsLive);
        Assert.Equal(LiveOrderStatus.Blocked, t.LiveStatus);
    }

    [Fact]
    public async Task Blocks_When_Notional_Exceeds_Cap()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, qty: 10m); // notional = 100*10 = 1000
        var factory = new FakeOrderFactory();
        var opts = Opts();
        opts.MaxNotionalUsdt = 50m;

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, opts).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await db.Trades.FindAsync(tradeId))!.LiveStatus);
    }

    [Fact]
    public async Task Does_Nothing_When_Master_Switch_Off()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts(enabled: false)).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.False(t!.IsLive);
        Assert.Equal(LiveOrderStatus.None, t.LiveStatus); // không đụng tới
    }

    [Fact]
    public async Task Blocks_When_Ai_Not_Configured()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts(), aiConfigured: false).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed); // bắt buộc AI → không gửi
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.False(t!.IsLive);
        Assert.Equal(LiveOrderStatus.Blocked, t.LiveStatus);
    }

    [Fact]
    public async Task Override_Risk_Places_Despite_Critical()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, withCritical: true);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts(), allowOverrideRisk: true).PlaceForTradeAsync(tradeId);

        Assert.Equal(3, factory.Provider.Placed.Count); // bỏ qua Critical → vẫn đặt entry+SL+TP
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.True((await db.Trades.FindAsync(tradeId))!.IsLive);
    }

    [Fact]
    public async Task Leverage_Cap_Blocks_Without_Override()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, lev: 50m); // > MaxLeverage 20
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await db.Trades.FindAsync(tradeId))!.LiveStatus);
    }

    [Fact]
    public async Task Override_Risk_Bypasses_Leverage_Cap()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, lev: 50m); // > MaxLeverage 20
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts(), allowOverrideRisk: true).PlaceForTradeAsync(tradeId);

        Assert.NotEmpty(factory.Provider.Placed); // override → vẫn đặt dù vượt cap đòn bẩy
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.True((await db.Trades.FindAsync(tradeId))!.IsLive);
    }

    [Fact]
    public async Task Block_Sets_Status_Cancelled()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, withCritical: true);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.Equal(TradeStatus.Cancelled, t!.Status); // 1-1: không vào sàn → Cancelled
        Assert.Equal(LiveOrderStatus.Blocked, t.LiveStatus);
    }

    [Fact]
    public async Task Blocks_When_Duplicate_Open_Trade()
    {
        using var p = BuildProvider();
        long accId, tradeBId;
        using (var scope = p.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
            var acc = new TradingAccount
            {
                Name = "D", Currency = "USDT", InitialBalance = 1000m, CurrentBalance = 1000m,
                ApiKey = "k", ApiSecret = "s", RiskSetting = new RiskSetting()
            };
            db.TradingAccounts.Add(acc);
            await db.SaveChangesAsync();
            accId = acc.Id;

            // Lệnh A: Short BTCUSDT entry 100 đang mở.
            db.Trades.Add(new Trade
            {
                TradingAccountId = accId, Symbol = "BTCUSDT", Direction = TradeDirection.Short,
                Status = TradeStatus.Open, OrderType = OrderType.Market,
                EntryPrice = 100m, StopLoss = 105m, TakeProfit = 90m, Quantity = 0.1m, Leverage = 5m, OpenedAt = DateTime.UtcNow,
            });
            // Lệnh B: Short BTCUSDT entry 100.2 (~ trùng tương đối).
            var b = new Trade
            {
                TradingAccountId = accId, Symbol = "BTCUSDT", Direction = TradeDirection.Short,
                Status = TradeStatus.Open, OrderType = OrderType.Market,
                EntryPrice = 100.2m, StopLoss = 105m, TakeProfit = 90m, Quantity = 0.1m, Leverage = 5m, OpenedAt = DateTime.UtcNow,
            };
            db.Trades.Add(b);
            await db.SaveChangesAsync();
            tradeBId = b.Id;
        }

        var factory = new FakeOrderFactory();
        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeBId);

        Assert.Empty(factory.Provider.Placed); // trùng → không gửi
        using var verify = p.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await vdb.Trades.FindAsync(tradeBId))!.LiveStatus);
    }

    [Fact]
    public async Task Is_Idempotent_On_Second_Call()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);
        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Equal(3, factory.Provider.Placed.Count); // không gửi lại lần 2
    }

    // --- Task 3: kiểm tra CHÍNH XÁC dữ liệu lệnh gửi lên sàn (loại, giá vào, SL, TP) ---

    [Fact]
    public async Task Market_Long_Sends_Correct_Entry_Sl_Tp_Data()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, qty: 0.1m, direction: TradeDirection.Long,
            orderType: OrderType.Market, entry: 100m, stopLoss: 95m, takeProfit: 110m);
        var factory = new FakeOrderFactory();
        var prov = factory.Provider;
        var opts = Opts();
        opts.MinOrderNotionalUsdt = 0m; // tắt sàn ép min-notional để kiểm tra ánh xạ qty gốc

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, opts).PlaceForTradeAsync(tradeId);

        Assert.Equal(3, prov.Placed.Count);

        // (1) Entry: MARKET, BUY, qty đúng, KHÔNG có price (market), positionSide LONG.
        var entry = prov.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}");
        Assert.Equal(FuturesOrderKind.Market, entry.Kind);
        Assert.Equal(OrderSide.Buy, entry.Side);
        Assert.Equal(FuturesPositionSide.Long, entry.PositionSide);
        Assert.Equal(0.1m, entry.Quantity);
        Assert.Null(entry.Price);
        Assert.False(entry.ClosePosition);

        // (2) SL: STOP_MARKET, SELL (đóng Long), stopPrice = 95, closePosition, positionSide LONG.
        var sl = prov.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}-sl");
        Assert.Equal(FuturesOrderKind.StopMarket, sl.Kind);
        Assert.Equal(OrderSide.Sell, sl.Side);
        Assert.Equal(FuturesPositionSide.Long, sl.PositionSide);
        Assert.Equal(95m, sl.StopPrice);
        Assert.True(sl.ClosePosition);
        Assert.Null(sl.Quantity);

        // (3) TP: TAKE_PROFIT_MARKET, SELL, stopPrice = 110, closePosition, positionSide LONG.
        var tp = prov.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}-tp");
        Assert.Equal(FuturesOrderKind.TakeProfitMarket, tp.Kind);
        Assert.Equal(OrderSide.Sell, tp.Side);
        Assert.Equal(FuturesPositionSide.Long, tp.PositionSide);
        Assert.Equal(110m, tp.StopPrice);
        Assert.True(tp.ClosePosition);
    }

    [Fact]
    public async Task Limit_Order_Sends_Entry_Price()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, direction: TradeDirection.Long,
            orderType: OrderType.Limit, entry: 100m, stopLoss: 95m, takeProfit: 110m);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        var entry = factory.Provider.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}");
        Assert.Equal(FuturesOrderKind.Limit, entry.Kind);
        Assert.Equal(100m, entry.Price); // LIMIT phải gửi giá vào
        Assert.Equal(OrderSide.Buy, entry.Side);
    }

    [Fact]
    public async Task Short_Order_Flips_Sides_And_Position()
    {
        using var p = BuildProvider();
        // Short hợp lệ: SL trên entry, TP dưới entry.
        var (_, tradeId) = await SeedAsync(p, direction: TradeDirection.Short,
            orderType: OrderType.Market, entry: 100m, stopLoss: 105m, takeProfit: 90m);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        var entry = factory.Provider.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}");
        Assert.Equal(OrderSide.Sell, entry.Side);                  // vào Short = SELL
        Assert.Equal(FuturesPositionSide.Short, entry.PositionSide);

        var sl = factory.Provider.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}-sl");
        Assert.Equal(OrderSide.Buy, sl.Side);                       // đóng Short = BUY
        Assert.Equal(FuturesPositionSide.Short, sl.PositionSide);
        Assert.Equal(105m, sl.StopPrice);

        var tp = factory.Provider.Placed.Single(r => r.NewClientOrderId == $"mmw-{tradeId}-tp");
        Assert.Equal(OrderSide.Buy, tp.Side);
        Assert.Equal(FuturesPositionSide.Short, tp.PositionSide);
        Assert.Equal(90m, tp.StopPrice);
    }

    [Fact]
    public async Task Blocks_When_StopLoss_Missing()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, direction: TradeDirection.Long,
            orderType: OrderType.Market, entry: 100m, stopLoss: null, takeProfit: 110m);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        // Validate mới: thiếu SL → chặn, không gửi bất kỳ lệnh nào lên sàn.
        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await db.Trades.FindAsync(tradeId))!.LiveStatus);
    }

    [Fact]
    public async Task Blocks_When_TakeProfit_Missing()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, direction: TradeDirection.Long,
            orderType: OrderType.Market, entry: 100m, stopLoss: 95m, takeProfit: null);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await db.Trades.FindAsync(tradeId))!.LiveStatus);
    }

    [Fact]
    public async Task Blocks_When_SL_Wrong_Side_Long()
    {
        using var p = BuildProvider();
        // Long mà SL > Entry → sai phía
        var (_, tradeId) = await SeedAsync(p, direction: TradeDirection.Long,
            orderType: OrderType.Market, entry: 100m, stopLoss: 105m, takeProfit: 110m);
        var factory = new FakeOrderFactory();

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        Assert.Empty(factory.Provider.Placed);
        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(LiveOrderStatus.Blocked, (await db.Trades.FindAsync(tradeId))!.LiveStatus);
    }

    // ── Retry tests ────────────────────────────────────────────────────────────

    private sealed class FailingOrderFactory : IExchangeOrderProviderFactory
    {
        public readonly FakeOrderProvider Provider = new();
        private readonly int _failPlaceCount;

        public FailingOrderFactory(int failPlaceCount) => _failPlaceCount = failPlaceCount;

        public IExchangeOrderProvider Create(string apiKey, string apiSecret, bool useTestnet)
            => new FailingProvider(Provider, _failPlaceCount);

        // Delegates base calls to FakeOrderProvider but fails the first N PlaceFuturesOrderAsync calls after entry.
        private sealed class FailingProvider : IExchangeOrderProvider
        {
            private readonly FakeOrderProvider _inner;
            private readonly int _failCount;
            private int _calls;

            public FailingProvider(FakeOrderProvider inner, int failCount)
            {
                _inner = inner;
                _failCount = failCount;
            }

            public Task<string> ValidateFuturesOrderAsync(FuturesOrderRequest req, CancellationToken ct = default)
                => Task.FromResult("OK");

            public Task<ExchangeOrderResult> PlaceFuturesOrderAsync(FuturesOrderRequest req, CancellationToken ct = default)
            {
                _calls++;
                // First call is entry — always succeed. Subsequent calls are SL/TP.
                if (_calls > 1 && _calls <= _failCount + 1)
                    throw new InvalidOperationException("Fake SL/TP failure");
                return _inner.PlaceFuturesOrderAsync(req, ct);
            }
            public Task SetLeverageAsync(string s, int l, CancellationToken ct) => _inner.SetLeverageAsync(s, l, ct);
            public Task<decimal> NormalizeQuantityAsync(string s, decimal q, CancellationToken ct) => _inner.NormalizeQuantityAsync(s, q, ct);
            public Task<decimal> NormalizeQuantityForNotionalAsync(string s, decimal q, decimal e, decimal m, CancellationToken ct) => _inner.NormalizeQuantityForNotionalAsync(s, q, e, m, ct);
            public Task CancelOrderAsync(string s, string id, CancellationToken ct) => _inner.CancelOrderAsync(s, id, ct);
            public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(string? s, CancellationToken ct) => _inner.GetOpenPositionsAsync(s, ct);
            public Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenOrdersAsync(string? s, CancellationToken ct) => _inner.GetOpenOrdersAsync(s, ct);
            public Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenConditionalOrdersAsync(string? s, CancellationToken ct) => _inner.GetOpenConditionalOrdersAsync(s, ct);
            public Task CancelAllOpenOrdersAsync(string s, CancellationToken ct) => _inner.CancelAllOpenOrdersAsync(s, ct);
            public Task ClosePositionAsync(string s, CancellationToken ct) => _inner.ClosePositionAsync(s, ct);
        }
    }

    [Fact]
    public async Task SltpPending_Set_When_All_Retries_Exhausted()
    {
        using var p = BuildProvider();
        var (_, tradeId) = await SeedAsync(p, entry: 100m, stopLoss: 95m, takeProfit: 110m);
        // Fail ALL 3 attempts for SL (calls 2–4), then also fail TP → entire retry block throws
        var factory = new FailingOrderFactory(failPlaceCount: 10); // always fail after entry

        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).PlaceForTradeAsync(tradeId);

        using var verify = p.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var t = await db.Trades.FindAsync(tradeId);
        Assert.True(t!.IsLive);                                          // entry vào OK
        Assert.Equal(LiveOrderStatus.SltpPending, t.LiveStatus);         // SL/TP chưa đặt được
    }

    [Fact]
    public async Task RetryPendingSltp_Calls_SyncLevels_And_Clears_Pending()
    {
        using var p = BuildProvider();
        var (accId, tradeId) = await SeedAsync(p, entry: 100m, stopLoss: 95m, takeProfit: 110m);

        // Manually mark trade as SltpPending (simulate scenario where entry succeeded but SL/TP failed)
        using (var seed = p.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<MmwDbContext>();
            var t = await db.Trades.FindAsync(tradeId);
            t!.IsLive = true;
            t.LiveStatus = LiveOrderStatus.SltpPending;
            t.Status = TradeStatus.Open;
            await db.SaveChangesAsync();
        }

        var factory = new FakeOrderFactory(); // will succeed
        using (var scope = p.CreateScope())
            await BuildService(scope, factory, Opts()).RetryPendingSltpAsync();

        using var verify = p.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var result = await db2.Trades.FindAsync(tradeId);
        Assert.Equal(LiveOrderStatus.Submitted, result!.LiveStatus);     // retry thành công
        // SL + TP orders placed (entry not placed again) + CancelAll called once
        Assert.Equal(1, factory.Provider.CancelAllCalls);
        Assert.Equal(2, factory.Provider.Placed.Count);                  // SL + TP only
    }
}
