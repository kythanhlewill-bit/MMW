using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json.Serialization;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Infrastructure;
using MMW.Web.Data;
using MMW.Web.Hubs;
using MMW.Web.Infrastructure;
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

var app = builder.Build();

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

    // Job quét thị trường: định kỳ mỗi 5 phút + chạy ngay 1 lần khi khởi động.
    RecurringJob.AddOrUpdate<IMarketScanService>(
        "market-scan",
        job => job.ScanAllAsync(CancellationToken.None),
        "*/5 * * * *");
    BackgroundJob.Enqueue<IMarketScanService>(job => job.ScanAllAsync(CancellationToken.None));

    // Job đồng bộ kết quả lệnh từ sàn: mỗi 2 phút.
    RecurringJob.AddOrUpdate<ITradeResultSyncService>(
        "trade-result-sync",
        job => job.SyncAllAccountsAsync(CancellationToken.None),
        "*/2 * * * *");

    // Job phân tích lệnh đang mở: mỗi 3 phút.
    RecurringJob.AddOrUpdate<ITradeAdvisorService>(
        "trade-advisor",
        job => job.AnalyzeOpenTradesAsync(CancellationToken.None),
        "*/1 * * * *");

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
