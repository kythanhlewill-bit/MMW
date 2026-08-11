using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json.Serialization;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Infrastructure;
using MMW.Web.Data;
using MMW.Web.Hubs;
using MMW.Web.Infrastructure;
using MMW.Web.Jobs;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

var webRootPath = builder.Environment.WebRootPath
    ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var logDirectory = builder.Environment.IsDevelopment()
    ? Path.Combine(webRootPath, "log")
    : Path.Combine(builder.Environment.ContentRootPath, "logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDirectory, $"file{DateTime.Now:MMddyyyy}-.txt"),
        rollingInterval: RollingInterval.Infinite,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Tầng nghiệp vụ + hạ tầng (DbContext code-first, repository, UnitOfWork, AutoMapper, services).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Hangfire — dùng chung SQL Server với app (tự tạo schema riêng).
var hangfireConnection = builder.Configuration.GetConnectionString("Default");
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireConnection));
builder.Services.AddHangfireServer();

// Xác thực bằng cookie — chưa đăng nhập sẽ bị đẩy về /Account/Login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "MMW.Auth";
        options.Cookie.HttpOnly = true;
    });

// Mọi endpoint mặc định yêu cầu đăng nhập (trừ nơi có [AllowAnonymous]).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotificationSender, SignalRNotificationSender>();
builder.Services.AddScoped<INotificationEmailQueue, HangfireNotificationEmailQueue>();
builder.Services.AddScoped<IEngineJobs, EngineJobs>();

// Chụp lại danh sách đăng ký để lệnh CLI `backtest` dựng được một scope thay hai cổng.
builder.Services.AddSingleton<IServiceCollectionSnapshot>(new ServiceCollectionSnapshot(builder.Services));

var app = builder.Build();

// Chế độ dòng lệnh: nạp kho hoặc chạy kiểm thử lịch sử rồi thoát, KHÔNG khởi động web.
if (BacktestCli.Handles(args))
{
    return await BacktestCli.RunAsync(args, app);
}

// Chạy sau reverse proxy (Nginx): tin cậy X-Forwarded-Proto/For để biết request gốc là https.
// Không có bước này thì UseHttpsRedirection bên dưới thấy scheme http và đẩy vòng lặp chuyển hướng,
// đồng thời cookie Secure + SignalR qua websocket không hoạt động.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
        ex is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Information;
});

app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard (chỉ user đã đăng nhập).
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<NotificationHub>("/hubs/notifications");

try
{
    // Tạo DB (nếu chưa có), áp migration và seed dữ liệu khởi tạo.
    await SeedData.InitializeAsync(app.Services);

    // Job quét thị trường bằng LLM: giữ chạy ở chế độ SO SÁNH SONG SONG, không còn là đường
    // sinh lệnh. Đường sinh lệnh giờ là `signal-eval` bên dưới, hoàn toàn tất định (FR-059).
    // Hạ nhịp xuống 15 phút cho khớp cây nến vào lệnh và giảm chi phí AI.
    RecurringJob.AddOrUpdate<IMarketScanService>(
        "market-scan-shadow",
        job => job.ScanAllAsync(CancellationToken.None),
        "*/15 * * * *");
    RecurringJob.RemoveIfExists("market-scan");

    // Job chấm điểm tất định trên cây nến 15m vừa đóng: 0 lời gọi AI (FR-025 → FR-034).
    // Trễ 1 phút so với mốc nến đóng theo R-011 — gọi đúng 00:00 thì sàn thường chưa chốt
    // xong cây nến, và nến chưa đóng bị cắt bỏ sẽ khiến chu kỳ chấm trên cây nến cũ.
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "signal-eval",
        job => job.RunSignalEvalAsync(CancellationToken.None),
        "1,16,31,46 * * * *");

    // Job đồng bộ kết quả lệnh từ sàn: mỗi 2 phút.
    RecurringJob.AddOrUpdate<ITradeResultSyncService>(
        "trade-result-sync",
        job => job.SyncAllAccountsAsync(CancellationToken.None),
        "*/2 * * * *");

    // Job phân tích lệnh đang mở: mỗi 3 phút. Cron trước đây là */1 dù chú thích ghi 3 phút —
    // phần tính máy chạy thừa 3 lần, và phần AI thì tốn tiền thật. Nhịp gọi AI còn bị chặn thêm
    // một lớp nữa bên trong service, xem TradeAdvisorService.ShouldAskLlm.
    RecurringJob.AddOrUpdate<ITradeAdvisorService>(
        "trade-advisor",
        job => job.AnalyzeOpenTradesAsync(CancellationToken.None),
        "*/3 * * * *");

    // Job quét lịch/tin vĩ mô: cảnh báo user trước vùng tin mạnh.
    RecurringJob.AddOrUpdate<IMacroEventService>(
        "macro-event-scan",
        job => job.ScanAndNotifyAsync(CancellationToken.None),
        "*/15 * * * *");
    BackgroundJob.Enqueue<IMacroEventService>(job => job.ScanAndNotifyAsync(CancellationToken.None));

    // Job retry SL/TP chưa đặt được (SltpPending): mỗi 2 phút.
    RecurringJob.AddOrUpdate<ILiveOrderService>(
        "sltp-retry",
        job => job.RetryPendingSltpAsync(CancellationToken.None),
        "*/2 * * * *");

    // Job làm phẳng vị thế trước cửa sổ chặn: mỗi phút, 0 lời gọi AI (FR-013, T063).
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "position-manage",
        job => job.RunPositionManageAsync(CancellationToken.None),
        "*/1 * * * *");

    // Job kiểm tra lịch sự kiện còn hạn không: 23:00 UTC, ngay trước khi lập kế hoạch ngày,
    // để trader thấy cảnh báo TRƯỚC phiên chứ không phải giữa phiên (FR-014).
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "calendar-freshness",
        job => job.RunCalendarFreshnessAsync(CancellationToken.None),
        "0 23 * * *");
    BackgroundJob.Enqueue<IEngineJobs>(job => job.RunCalendarFreshnessAsync(CancellationToken.None));

    // Job chụp kho lịch sử: mỗi giờ. Dữ liệu phái sinh KHÔNG lấy lại được về sau, nên phải
    // dựng dần từ hôm nay để kiểm thử tương lai chạy được đủ 100 điểm (T139, giảm rủi ro R-003).
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "archive-snapshot",
        job => job.RunArchiveSnapshotAsync(CancellationToken.None),
        "5 * * * *");

    // Chấm kết cục phiếu: 25 phút sau job chụp kho, để ăn đúng phần nến vừa được nạp về.
    // Đây là thước đo các CỔNG veto — phiếu bị chặn rồi giá đi ngược nghĩa là cổng cứu được một
    // lệnh lỗ, bị chặn mà giá chạm mục tiêu nghĩa là cổng chặn nhầm. Không chạy job này thì cả
    // hai vế đều không có số liệu và mọi tranh luận về cổng quay về cảm nhận.
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "scorecard-outcome-review",
        job => job.RunScorecardOutcomeReviewAsync(CancellationToken.None),
        "30 * * * *");

    // Job lập kế hoạch ngày: 23:30 UTC, sinh cho ngày UTC kế tiếp, 1 lời gọi AI (FR-016).
    RecurringJob.AddOrUpdate<IEngineJobs>(
        "daily-plan",
        job => job.RunDailyPlanAsync(CancellationToken.None),
        "30 23 * * *");

    RecurringJob.AddOrUpdate<IEngineJobs>(
        "news-scan",
        job => job.RunNewsScanAsync(CancellationToken.None),
        "*/15 * * * *");

    // Bù kế hoạch của HÔM NAY khi ứng dụng khởi động giữa ngày mà chưa có bản nào — không có
    // kế hoạch nghĩa là cả ngày không giao dịch được (FR-023).
    BackgroundJob.Enqueue<IEngineJobs>(job => job.RunDailyPlanCatchUpAsync(CancellationToken.None));

    Log.Information("Starting MMW Web in {Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MMW Web terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

return 0;
