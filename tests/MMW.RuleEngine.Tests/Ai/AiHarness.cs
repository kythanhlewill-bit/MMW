using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Models;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Infrastructure.Repositories;
using MMW.RuleEngine.Tests.TimeGuard;
using MMW.Shared.Interfaces;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// Bộ khung cho các test lớp bối cảnh AI: một tài khoản, một mô hình ngôn ngữ GIẢ do test
/// điều khiển, và một nguồn tin giả.
/// </summary>
/// <remarks>
/// Mọi phản hồi trong các test dưới đây đều là phản hồi CỐ TÌNH DỊ THƯỜNG. Đó là điểm chính:
/// lớp này không được kiểm chứng bằng câu trả lời đẹp, vì câu trả lời đẹp thì lớp nào cũng qua.
/// </remarks>
internal sealed class AiHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private AiHarness(ServiceProvider provider, long accountId, FakeLlm llm, FakeHeadlineFeed headlines, TestClock clock)
    {
        _provider = provider;
        AccountId = accountId;
        Llm = llm;
        Headlines = headlines;
        Clock = clock;
    }

    public long AccountId { get; }
    public FakeLlm Llm { get; }
    public FakeHeadlineFeed Headlines { get; }
    public TestClock Clock { get; }

    public static async Task<AiHarness> CreateAsync(Action<EngineSetting>? configure = null)
    {
        var llm = new FakeLlm();
        var headlines = new FakeHeadlineFeed();
        var marketData = new FakeMarketData();
        var clock = new TestClock(TestClock.Default);

        var dbName = "mmw_ai_" + Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddDbContext<MmwDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MmwDbContext>());
        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddApplication();
        services.AddSingleton<IMarketDataProvider>(marketData);
        services.AddSingleton<IMarketSentimentProvider>(marketData);
        services.AddSingleton<Application.Abstractions.IClock>(clock);
        services.AddSingleton<ILlmService>(llm);
        services.AddSingleton<IMacroEventProvider>(headlines);

        // PositionManageService giờ đẩy dừng lỗ hoà vốn lên sàn, nên nó kéo theo ILiveOrderService.
        // Bản thật cần cả nhà máy provider sàn — thứ mà bộ khung đo ngân sách gọi AI không có và
        // không cần. Xem FakeLiveOrders.
        services.AddSingleton<ILiveOrderService>(new FakeLiveOrders());

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

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

        return new AiHarness(provider, account.Id, llm, headlines, clock);
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    public T Resolve<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>Kế hoạch ngày ĐÃ HOÀN CHỈNH — lớp AI chỉ được làm giàu, không được sinh ra nó.</summary>
    public async Task<DailyPlan> AddPlanAsync(
        DateOnly? planDate = null,
        AllowedDirections directions = AllowedDirections.LongOnly,
        decimal riskMultiplier = 1.0m,
        int maxTrades = 5)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        var plan = new DailyPlan
        {
            TradingAccountId = AccountId,
            PlanDateUtc = planDate ?? DateOnly.FromDateTime(Clock.UtcNow),
            GeneratedAtUtc = Clock.UtcNow,
            DayRegime = DayRegime.TrendUp,
            VolatilityRegime = VolatilityRegime.Normal,
            AllowedDirections = directions,
            RiskMultiplier = riskMultiplier,
            MaxTradesToday = maxTrades,
            IsComplete = true,
        };

        db.DailyPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    public async Task AddEventsAsync(params ScheduledEvent[] events)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        db.ScheduledEvents.AddRange(events);
        await db.SaveChangesAsync();
    }

    public async Task<List<MarketContextRecord>> ContextRecordsAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        return await db.MarketContextRecords.AsNoTracking().ToListAsync();
    }

    public async Task<List<ScheduledEvent>> AiEventsAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        return await db.ScheduledEvents
            .Where(e => e.Origin == ScheduledEventOrigin.AiDetected)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<DailyPlan> ReloadPlanAsync(long planId)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        return await db.DailyPlans.AsNoTracking().FirstAsync(p => p.Id == planId);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Mô hình ngôn ngữ giả: phản hồi và số lần gọi do test kiểm soát hoàn toàn.
/// </summary>
/// <remarks>
/// <see cref="CallCount"/> tồn tại vì hai điều kiện chấp nhận đo bằng SỐ LẦN GỌI chứ không
/// bằng kết quả: SC-005 (dưới 30 lần/ngày) và FR-049 (vòng chấm điểm gọi đúng 0 lần).
/// </remarks>
internal sealed class FakeLlm : ILlmService
{
    private readonly Queue<string?> _queued = new();

    public bool Configured { get; set; } = true;
    public bool Throws { get; set; }

    /// <summary>Phản hồi mặc định khi hàng đợi rỗng.</summary>
    public string? DefaultResponse { get; set; }

    public int CallCount { get; private set; }
    public List<string> SystemPrompts { get; } = new();

    public bool IsConfigured => Configured;

    public void Enqueue(params string?[] responses)
    {
        foreach (var r in responses) _queued.Enqueue(r);
    }

    public Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        CallCount++;
        SystemPrompts.Add(systemPrompt);

        if (Throws) throw new HttpRequestException("Giả lập lớp AI chết hoàn toàn.");

        return Task.FromResult(_queued.Count > 0 ? _queued.Dequeue() : DefaultResponse);
    }
}

/// <summary>Nguồn tin giả. Mỗi mục có <c>SourceKey</c> riêng để thử cơ chế chống xử lý lại.</summary>
internal sealed class FakeHeadlineFeed : IMacroEventProvider
{
    public List<MacroEventModel> Items { get; } = new();

    public bool Configured { get; set; } = true;
    public bool Throws { get; set; }

    public bool IsConfigured => Configured;

    public void Add(string sourceKey, string title, MacroEventImpact impact = MacroEventImpact.Medium) =>
        Items.Add(new MacroEventModel
        {
            Source = "test",
            SourceKey = sourceKey,
            Kind = MacroEventKind.MarketNews,
            Impact = impact,
            Title = title,
        });

    public Task<IReadOnlyList<MacroEventModel>> GetEventsAsync(
        DateTime utcNow, TimeSpan lookAhead, TimeSpan newsLookBack, CancellationToken cancellationToken = default)
    {
        if (Throws) throw new HttpRequestException("Giả lập nguồn tin lỗi.");
        return Task.FromResult<IReadOnlyList<MacroEventModel>>(Items.ToList());
    }
}
