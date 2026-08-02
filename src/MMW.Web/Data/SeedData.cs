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
                AutoCreateTradeFromSignal = false,
                MinSignalScore = 2,
            });
            await db.SaveChangesAsync();
        }

        await SeedEngineSettingsAsync(db);
    }

    /// <summary>
    /// Tạo <see cref="EngineSetting"/> kèm bảng phiên và luật chặn cho mọi tài khoản chưa có.
    /// </summary>
    /// <remarks>
    /// Idempotent: chỉ tạo cho tài khoản còn thiếu, không đụng vào cấu hình đã có.
    /// Người dùng chỉnh ngưỡng rồi mà seeder ghi đè khi khởi động lại thì Nguyên tắc I
    /// chỉ còn là hình thức.
    /// </remarks>
    private static async Task SeedEngineSettingsAsync(MmwDbContext db)
    {
        var accountIds = await db.TradingAccounts.Select(a => a.Id).ToListAsync();
        var configured = await db.EngineSettings.Select(e => e.TradingAccountId).ToListAsync();

        var missing = accountIds.Except(configured).ToList();
        if (missing.Count == 0) return;

        foreach (var accountId in missing)
        {
            var setting = new EngineSetting { TradingAccountId = accountId };

            foreach (var row in DefaultSessionQualityRows()) setting.SessionQualityRows.Add(row);
            foreach (var rule in DefaultBlackoutRules()) setting.BlackoutRules.Add(rule);

            var errors = setting.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cấu hình engine mặc định không hợp lệ — sửa seed trước khi chạy tiếp:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, errors));
            }

            db.EngineSettings.Add(setting);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Bảng chất lượng phiên cold-start, dùng khi chưa đủ số lệnh để thống kê giờ cá nhân.</summary>
    private static IEnumerable<SessionQualityRow> DefaultSessionQualityRows() => new[]
    {
        new SessionQualityRow { FromHourUtc = 0,  ToHourUtc = 7,  Score = 2, Label = "Phiên Á" },
        new SessionQualityRow { FromHourUtc = 7,  ToHourUtc = 9,  Score = 5, Label = "Mở cửa London" },
        new SessionQualityRow { FromHourUtc = 9,  ToHourUtc = 13, Score = 5, Label = "London" },
        new SessionQualityRow { FromHourUtc = 13, ToHourUtc = 16, Score = 6, Label = "Chồng lấn New York" },
        new SessionQualityRow { FromHourUtc = 16, ToHourUtc = 21, Score = 4, Label = "New York chiều" },
        new SessionQualityRow { FromHourUtc = 21, ToHourUtc = 24, Score = 1, Label = "Đêm mỏng" },
    };

    /// <summary>
    /// Độ rộng cửa sổ chặn theo FR-010.
    /// </summary>
    /// <remarks>
    /// FR-010 liệt kê 8 NHÓM sự kiện, nhưng khoá duy nhất của bảng là (EngineSettingId, EventKind)
    /// nên phải trải thành 12 dòng — một dòng cho mỗi loại. Các loại cùng nhóm dùng chung độ rộng.
    ///
    /// Ba loại có <c>MinutesBefore = MinutesAfter = 0</c> là có chủ ý: họp báo chính sách và
    /// khoảng trống cuối tuần được chặn theo ĐỘ DÀI của chính sự kiện, không theo biên trước/sau.
    /// </remarks>
    private static IEnumerable<BlackoutRule> DefaultBlackoutRules() => new[]
    {
        // Nhóm 1 — số liệu lạm phát và việc làm, T−60 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.Cpi, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Ppi, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Nfp, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 2 — công bố quyết định chính sách, T−90 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.FomcStatement, MinutesBefore = 90, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 3 — họp báo: chặn trọn độ dài buổi họp
        new BlackoutRule { EventKind = ScheduledEventKind.FomcPressConference, MinutesBefore = 0, MinutesAfter = 0, RequiresPositionAction = true },

        // Nhóm 4 — số liệu tác động vừa, T−30 → T+15
        new BlackoutRule { EventKind = ScheduledEventKind.Pce, MinutesBefore = 30, MinutesAfter = 15, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Gdp, MinutesBefore = 30, MinutesAfter = 15 },
        new BlackoutRule { EventKind = ScheduledEventKind.JoblessClaims, MinutesBefore = 30, MinutesAfter = 15 },

        // Nhóm 5 — đáo hạn quyền chọn, T−30 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.OptionsExpiry, MinutesBefore = 30, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 6 — thanh toán phí vốn, T−5 → T+5
        new BlackoutRule { EventKind = ScheduledEventKind.FundingSettlement, MinutesBefore = 5, MinutesAfter = 5 },

        // Nhóm 7 — khoảng trống cuối tuần: chặn trọn độ dài sự kiện (21:00–23:00 UTC Chủ nhật)
        new BlackoutRule { EventKind = ScheduledEventKind.WeekendGap, MinutesBefore = 0, MinutesAfter = 0 },

        // Nhóm 8 — tin đột xuất, T → T+60
        new BlackoutRule { EventKind = ScheduledEventKind.AiDetectedShock, MinutesBefore = 0, MinutesAfter = 60, RequiresPositionAction = true },
    };
}
