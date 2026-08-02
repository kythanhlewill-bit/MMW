using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MMW.Application.Abstractions;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.DbContext;
using MMW.Infrastructure.Abstractions;
using MMW.Infrastructure.Ai;
using MMW.Infrastructure.Email;
using MMW.Infrastructure.Exchanges.Binance;
using MMW.Infrastructure.MacroEvents;
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

        // --- Cổng thời gian: nguồn thời gian duy nhất của tầng quyết định (R-001) ---
        services.AddSingleton<IClock, SystemClock>();

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

        services.AddHttpClient<ISymbolSearchService, BinanceSymbolSearchService>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<BinanceOptions>>().Value;
            client.BaseAddress = new Uri(opt.FuturesApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Chỉ số tâm lý thị trường — công khai, không cần khoá (R-004).
        services.AddHttpClient<IMarketSentimentProvider, MarketSentiment.AlternativeMeFearGreedProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Factory cho multi-account (mỗi account có API key riêng).
        services.AddHttpClient("BinanceApi");
        services.AddScoped<IExchangeAccountProviderFactory, BinanceAccountProviderFactory>();

        // --- Đặt lệnh THẬT (live trading, mặc định tắt qua LiveTradingOptions) ---
        services.Configure<LiveTradingOptions>(configuration.GetSection(LiveTradingOptions.SectionName));
        services.AddHttpClient("BinanceFutures");
        services.AddScoped<IExchangeOrderProviderFactory, BinanceFuturesOrderProviderFactory>();

        // --- Macro calendar / central-bank news context ---
        services.Configure<MacroEventOptions>(configuration.GetSection(MacroEventOptions.Section));
        services.AddHttpClient<IMacroEventProvider, ConfiguredMacroEventProvider>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<MacroEventOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds > 0 ? opt.TimeoutSeconds : 15);
        });

        // --- AI / LLM ---
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        var llmOptions = configuration.GetSection(LlmOptions.Section).Get<LlmOptions>() ?? new LlmOptions();
        var provider = llmOptions.Provider?.Trim();
        if (string.Equals(provider, "DeepSeek", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ILlmService, DeepSeekLlmService>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl)
                    ? "https://api.deepseek.com"
                    : opt.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds > 0 ? opt.TimeoutSeconds : 30);
            });
        }
        else if (string.Equals(provider, "MiniMax", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ILlmService, MiniMaxLlmService>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(opt.BaseUrl))
                    client.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
                if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds > 0 ? opt.TimeoutSeconds : 30);
            });
        }
        else
        {
            services.AddHttpClient<ILlmService, GeminiLlmService>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl)
                    ? "https://generativelanguage.googleapis.com"
                    : opt.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds > 0 ? opt.TimeoutSeconds : 30);
            });
        }

        // --- Email notifications ---
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
