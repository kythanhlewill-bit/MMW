using Microsoft.EntityFrameworkCore;
using MMW.Application.Interfaces;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Data;

public static class SeedData
{
    /// <summary>
    /// Áp dụng migration và tạo dữ liệu khởi tạo. Admin chỉ được tạo khi credential
    /// bootstrap được cấp qua cấu hình ngoài source (ưu tiên User Secrets).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<MmwDbContext>();
        await db.Database.MigrateAsync();

        var auth = sp.GetRequiredService<IAuthService>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

        // Không sinh credential mặc định có thể đoán được. Database mới chỉ có admin
        // khi operator chủ động cấu hình BootstrapAdmin qua User Secrets/environment.
        if (!await db.Users.AnyAsync())
        {
            var username = configuration["BootstrapAdmin:Username"];
            var password = configuration["BootstrapAdmin:Password"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                db.Users.Add(new User
                {
                    Username = username.Trim(),
                    DisplayName = "Administrator",
                    PasswordHash = auth.HashPassword(password),
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }
            else
            {
                logger.LogWarning(
                    "Database chưa có user. Cấu hình BootstrapAdmin:Username và BootstrapAdmin:Password bằng User Secrets để tạo admin đầu tiên.");
            }
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
        await SeedScheduledEventsAsync(db);
    }

    /// <summary>
    /// Tạo <see cref="EngineSetting"/> kèm bảng phiên và luật chặn cho mọi tài khoản chưa có.
    /// </summary>
    /// <remarks>
    /// Idempotent: chỉ tạo cho tài khoản còn thiếu, không đụng vào cấu hình đã có.
    /// Người dùng chỉnh ngưỡng rồi mà seeder ghi đè khi khởi động lại thì Nguyên tắc I
    /// chỉ còn là hình thức.
    ///
    /// Giá trị mặc định lấy từ <see cref="EngineSettingDefaults"/> — dùng chung với kiểm thử,
    /// để test không chứng minh cho một bảng luật khác với bảng chạy thật.
    /// </remarks>
    private static async Task SeedEngineSettingsAsync(MmwDbContext db)
    {
        var accountIds = await db.TradingAccounts.Select(a => a.Id).ToListAsync();
        var configured = await db.EngineSettings.Select(e => e.TradingAccountId).ToListAsync();

        var missing = accountIds.Except(configured).ToList();
        if (missing.Count == 0) return;

        foreach (var accountId in missing)
        {
            var setting = EngineSettingDefaults.Create(accountId);

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

    /// <summary>
    /// Nạp lịch sự kiện vĩ mô suy ra được bằng quy tắc công bố (T060, R-005).
    /// </summary>
    /// <remarks>
    /// CHỈ nạp những sự kiện mà quy tắc công bố xác định được chắc chắn — hiện là NFP, theo
    /// thông lệ "thứ Sáu đầu tiên của tháng, 8:30 sáng giờ New York" của BLS.
    ///
    /// CPI, PPI, PCE và các cuộc họp FOMC KHÔNG suy ra được: chúng do BLS và Fed công bố trước
    /// theo danh sách rời rạc. Chúng phải được dán tay vào <see cref="ManualCalendar2026"/> từ
    /// lịch chính thức. Đoán ngày cho chúng còn tệ hơn để trống — một cửa sổ chặn đặt sai giờ
    /// vừa bỏ lỡ tin thật vừa cấm nhầm một khung giờ lành.
    ///
    /// Idempotent theo <c>SourceKey</c>: chạy lại không sinh bản ghi trùng.
    /// </remarks>
    private static async Task SeedScheduledEventsAsync(MmwDbContext db)
    {
        var thisYear = DateTime.UtcNow.Year;

        var events = NonFarmPayrolls(thisYear)
            .Concat(NonFarmPayrolls(thisYear + 1))
            .Concat(ManualCalendar2026())
            .ToList();

        var keys = events.Select(e => e.SourceKey!).ToList();
        var existing = await db.ScheduledEvents
            .Where(e => e.SourceKey != null && keys.Contains(e.SourceKey))
            .Select(e => e.SourceKey!)
            .ToListAsync();

        var known = new HashSet<string>(existing, StringComparer.Ordinal);
        var fresh = events.Where(e => !known.Contains(e.SourceKey!)).ToList();
        if (fresh.Count == 0) return;

        db.ScheduledEvents.AddRange(fresh);
        await db.SaveChangesAsync();
    }

    /// <summary>Bảng công bố việc làm phi nông nghiệp: thứ Sáu đầu tiên của tháng, 8:30 giờ New York.</summary>
    private static IEnumerable<ScheduledEvent> NonFarmPayrolls(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var offset = ((int)DayOfWeek.Friday - (int)first.DayOfWeek + 7) % 7;
            var friday = first.AddDays(offset);

            yield return new ScheduledEvent
            {
                Kind = ScheduledEventKind.Nfp,
                Title = $"Việc làm phi nông nghiệp (NFP) tháng {month}/{year}",
                OccursAtUtc = EasternToUtc(friday, hour: 8, minute: 30),
                Impact = MacroEventImpact.Critical,
                Origin = ScheduledEventOrigin.Seeded,
                Currency = "USD",
                SourceKey = $"bls:nfp:{year}-{month:D2}",
                Notes = "Suy ra từ thông lệ thứ Sáu đầu tháng của BLS. Đối chiếu lịch chính thức nếu lệch.",
            };
        }
    }

    /// <summary>Lịch CPI / PPI / PCE / FOMC 2026 từ BLS, BEA và Federal Reserve.</summary>
    /// <remarks>
    /// Lưu ý khi điền: giờ công bố là 8:30 sáng New York, tức 12:30 UTC vào mùa hè (EDT) và
    /// 13:30 UTC vào mùa đông (EST) — dùng <see cref="EasternToUtc"/> thay vì gõ thẳng giờ UTC.
    /// Công bố quyết định FOMC là 14:00 New York, họp báo 14:30 kéo dài khoảng 60 phút.
    /// </remarks>
    private static IEnumerable<ScheduledEvent> ManualCalendar2026()
    {
        // BLS — Schedule of Releases for the Consumer Price Index, kiểm tra 03/08/2026.
        foreach (var date in Dates(
                     (1, 13), (2, 13), (3, 11), (4, 10), (5, 12), (6, 10),
                     (7, 14), (8, 12), (9, 11), (10, 14), (11, 10), (12, 10)))
        {
            yield return OfficialRelease(
                ScheduledEventKind.Cpi,
                $"Chỉ số giá tiêu dùng Hoa Kỳ (CPI) — {date:dd/MM/yyyy}",
                date, 8, 30, $"bls:cpi:release:{date:yyyy-MM-dd}",
                "BLS: https://www.bls.gov/schedule/news_release/cpi.htm");
        }

        // BLS — năm 2026 có hai lần công bố PPI trong tháng 1 do lịch hậu shutdown.
        foreach (var date in Dates(
                     (1, 14), (1, 30), (2, 27), (3, 18), (4, 14), (5, 13), (6, 11),
                     (7, 15), (8, 13), (9, 10), (10, 15), (11, 13), (12, 15)))
        {
            yield return OfficialRelease(
                ScheduledEventKind.Ppi,
                $"Chỉ số giá sản xuất Hoa Kỳ (PPI) — {date:dd/MM/yyyy}",
                date, 8, 30, $"bls:ppi:release:{date:yyyy-MM-dd}",
                "BLS: https://www.bls.gov/schedule/news_release/ppi.htm");
        }

        // BEA — Personal Income and Outlays (chứa PCE Price Index). Ngày 22/01 phát lúc
        // 10:00 ET; các lần còn lại phát lúc 08:30 ET.
        foreach (var release in TimedDates(
                     (1, 22, 10, 0), (2, 20, 8, 30), (3, 13, 8, 30), (4, 9, 8, 30),
                     (4, 30, 8, 30), (5, 28, 8, 30), (6, 25, 8, 30), (7, 30, 8, 30),
                     (8, 26, 8, 30), (9, 30, 8, 30), (10, 29, 8, 30), (11, 25, 8, 30),
                     (12, 23, 8, 30)))
        {
            yield return OfficialRelease(
                ScheduledEventKind.Pce,
                $"Thu nhập, chi tiêu cá nhân và PCE Hoa Kỳ — {release.Date:dd/MM/yyyy}",
                release.Date, release.Hour, release.Minute,
                $"bea:pce:release:{release.Date:yyyy-MM-dd}",
                "BEA: https://www.bea.gov/news/schedule");
        }

        // Federal Reserve — ngày thứ hai của tám cuộc họp định kỳ, là ngày phát statement.
        foreach (var date in Dates(
                     (1, 28), (3, 18), (4, 29), (6, 17),
                     (7, 29), (9, 16), (10, 28), (12, 9)))
        {
            yield return OfficialRelease(
                ScheduledEventKind.FomcStatement,
                $"Tuyên bố chính sách FOMC — {date:dd/MM/yyyy}",
                date, 14, 0, $"fed:fomc:statement:{date:yyyy-MM-dd}",
                "Federal Reserve: https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm");

            yield return OfficialRelease(
                ScheduledEventKind.FomcPressConference,
                $"Họp báo FOMC — {date:dd/MM/yyyy}",
                date, 14, 30, $"fed:fomc:press-conference:{date:yyyy-MM-dd}",
                "Federal Reserve: https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm",
                durationMinutes: 60);
        }
    }

    private static IEnumerable<DateTime> Dates(params (int Month, int Day)[] dates) =>
        dates.Select(d => new DateTime(2026, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc));

    private static IEnumerable<(DateTime Date, int Hour, int Minute)> TimedDates(
        params (int Month, int Day, int Hour, int Minute)[] dates) =>
        dates.Select(d =>
            (new DateTime(2026, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc), d.Hour, d.Minute));

    private static ScheduledEvent OfficialRelease(
        ScheduledEventKind kind,
        string title,
        DateTime date,
        int hour,
        int minute,
        string sourceKey,
        string notes,
        int? durationMinutes = null) => new()
    {
        Kind = kind,
        Title = title,
        OccursAtUtc = EasternToUtc(date, hour, minute),
        DurationMinutes = durationMinutes,
        Impact = MacroEventImpact.Critical,
        Origin = ScheduledEventOrigin.Seeded,
        Currency = "USD",
        SourceKey = sourceKey,
        Notes = $"Lịch công bố chính thức, đối chiếu ngày 03/08/2026. {notes}",
    };

    /// <summary>
    /// Đổi giờ New York sang UTC, có tính giờ mùa hè của Mỹ (chủ nhật thứ hai của tháng 3 đến
    /// chủ nhật đầu tiên của tháng 11).
    /// </summary>
    /// <remarks>
    /// Không dùng <c>TimeZoneInfo</c> vì tên múi giờ khác nhau giữa Windows và Linux, và một
    /// ngoại lệ "không tìm thấy múi giờ" lúc khởi động sẽ chặn cả seeder.
    /// </remarks>
    private static DateTime EasternToUtc(DateTime dateUtc, int hour, int minute)
    {
        var local = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, hour, minute, 0, DateTimeKind.Utc);
        return local.AddHours(IsUsDaylightTime(local) ? 4 : 5);
    }

    private static bool IsUsDaylightTime(DateTime date)
    {
        var marchFirst = new DateTime(date.Year, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var dstStart = marchFirst
            .AddDays(((int)DayOfWeek.Sunday - (int)marchFirst.DayOfWeek + 7) % 7)
            .AddDays(7);

        var novemberFirst = new DateTime(date.Year, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var dstEnd = novemberFirst
            .AddDays(((int)DayOfWeek.Sunday - (int)novemberFirst.DayOfWeek + 7) % 7);

        return date >= dstStart && date < dstEnd;
    }
}
