using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.DbContext;
using MMW.Infrastructure.Ai;
using MMW.Infrastructure.Exchanges.Binance;
using MMW.Infrastructure.Repositories;
using MMW.Shared.Interfaces;

namespace MMW.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký DbContext + repository pattern cho tầng Infrastructure.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Server=localhost;Database=MMW;Trusted_Connection=True;TrustServerCertificate=True;";

        services.AddDbContext<MmwDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName);
                sql.EnableRetryOnFailure();
            }));

        // Cho phép resolve DbContext gốc (BaseRepository nhận DbContext).
        services.AddScoped<Microsoft.EntityFrameworkCore.DbContext>(sp => sp.GetRequiredService<MmwDbContext>());

        services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // --- Tích hợp sàn (Binance) ---
        services.Configure<BinanceOptions>(configuration.GetSection(BinanceOptions.SectionName));
        services.AddMemoryCache();

        services.AddHttpClient<IMarketDataProvider, BinanceMarketDataProvider>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<BinanceOptions>>().Value;
            client.BaseAddress = new Uri(opt.MarketDataBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<IExchangeAccountProvider, BinanceAccountProvider>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<BinanceOptions>>().Value;
            client.BaseAddress = new Uri(opt.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Factory cho multi-account (mỗi account có API key riêng).
        services.AddHttpClient("BinanceApi");
        services.AddScoped<IExchangeAccountProviderFactory, BinanceAccountProviderFactory>();

        // --- AI / LLM ---
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        services.AddHttpClient<ILlmService, MiniMaxLlmService>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opt.BaseUrl))
                client.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds > 0 ? opt.TimeoutSeconds : 30);
        });

        return services;
    }
}
