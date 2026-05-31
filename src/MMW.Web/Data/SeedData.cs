using Microsoft.EntityFrameworkCore;
using MMW.Application.Interfaces;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Data;

public static class SeedData
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "Admin@123";

    /// <summary>
    /// Áp dụng migration và tạo dữ liệu khởi tạo (user admin + tài khoản demo) nếu chưa có.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<MmwDbContext>();
        await db.Database.MigrateAsync();

        var auth = sp.GetRequiredService<IAuthService>();

        // User admin mặc định
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User
            {
                Username = DefaultUsername,
                DisplayName = "Administrator",
                PasswordHash = auth.HashPassword(DefaultPassword),
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        // Tài khoản giao dịch demo + cấu hình rủi ro mặc định
        if (!await db.TradingAccounts.AnyAsync())
        {
            var account = new TradingAccount
            {
                Name = "Demo Binance",
                Broker = Broker.Binance,
                Currency = "USDT",
                InitialBalance = 1000m,
                CurrentBalance = 1000m,
                IsActive = true,
                RiskSetting = new RiskSetting(),
            };
            db.TradingAccounts.Add(account);
            await db.SaveChangesAsync();
        }

        // Watchlist mặc định cho job scan.
        if (!await db.WatchItems.AnyAsync())
        {
            db.WatchItems.AddRange(
                new WatchItem { Symbol = "BTCUSDT", Interval = "1h" },
                new WatchItem { Symbol = "ETHUSDT", Interval = "1h" },
                new WatchItem { Symbol = "BNBUSDT", Interval = "1h" },
                new WatchItem { Symbol = "SOLUSDT", Interval = "1h" });
            await db.SaveChangesAsync();
        }

        // Cấu hình toàn cục mặc định (tài khoản mặc định = tài khoản active đầu tiên).
        if (!await db.AppSettings.AnyAsync())
        {
            var firstAccount = await db.TradingAccounts.OrderBy(a => a.Id).FirstOrDefaultAsync();
            db.AppSettings.Add(new AppSetting
            {
                DefaultTradingAccountId = firstAccount?.Id,
                ConfirmBeforeCreateTrade = true,
                MinSignalScore = 2,
            });
            await db.SaveChangesAsync();
        }
    }
}
