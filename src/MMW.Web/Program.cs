using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using MMW.Application;
using MMW.Application.Interfaces;
using MMW.Infrastructure;
using MMW.Web.Data;
using MMW.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

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

app.Run();
