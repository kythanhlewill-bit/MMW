using System.Globalization;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Abstractions;
using MMW.Application.Backtest;
using MMW.Application.Backtest.Models;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Web.Data;

namespace MMW.Web.Jobs;

/// <summary>
/// Hai lệnh dòng lệnh: <c>backfill</c> nạp kho, <c>backtest</c> chạy kiểm thử (T137).
/// </summary>
/// <remarks>
/// Chạy kiểm thử KHÔNG làm được trong một request web: nó cần thay hai cổng <c>IClock</c> và
/// <c>IMarketDataProvider</c> cho toàn bộ vòng lặp, còn scope của request thì đang gắn với
/// đồng hồ thật. Một tiến trình riêng là chỗ sạch sẽ nhất để làm việc đó — và nó cũng khiến
/// việc "lỡ tay chạy kiểm thử trên đồng hồ thật" trở thành bất khả thi thay vì chỉ khó xảy ra.
/// </remarks>
public static class BacktestCli
{
    /// <summary>Đúng khi tham số dòng lệnh yêu cầu chạy CLI thay vì khởi động web.</summary>
    public static bool Handles(string[] args) =>
        args.Length > 0 && args[0] is "backfill" or "backtest" or "ordertest";

    public static async Task<int> RunAsync(string[] args, WebApplication app)
    {
        await SeedData.InitializeAsync(app.Services);

        return args[0] switch
        {
            "backfill" => await BackfillAsync(args, app),
            "backtest" => await BacktestAsync(args, app),
            "ordertest" => await OrderTestAsync(args, app),
            _ => Usage(),
        };
    }

    /// <summary><c>backfill --symbols BTCUSDT,ETHUSDT --intervals 15m,4h,1d --from ... --to ...</c></summary>
    private static async Task<int> BackfillAsync(string[] args, WebApplication app)
    {
        var options = Options(args);
        if (!TryOption(options, "symbols", "symbol", out var symbolsText)
            || !TryOption(options, "intervals", "interval", out var intervalsText)
            || !options.TryGetValue("from", out var fromText)
            || !TryDate(fromText, out var from)) return Usage();

        var to = DateTime.UtcNow;
        if (options.TryGetValue("to", out var toText) && !TryDate(toText, out to)) return Usage();
        if (to <= from) return Usage();

        var symbols = Csv(symbolsText).Select(x => x.ToUpperInvariant()).ToList();
        var intervals = Csv(intervalsText).ToList();
        if (symbols.Count == 0 || intervals.Count == 0) return Usage();

        using var scope = app.Services.CreateScope();
        var archive = scope.ServiceProvider.GetRequiredService<IKlineArchiveService>();
        var totalCandles = 0;
        var totalFunding = 0;
        var timer = Stopwatch.StartNew();

        foreach (var symbol in symbols)
        {
            var funding = await archive.BackfillFundingAsync(symbol, from, to);
            totalFunding += funding;
            Console.WriteLine($"{symbol}: đã nạp {funding} mốc phí vốn.");

            foreach (var interval in intervals)
            {
                var candles = await archive.BackfillAsync(symbol, interval, from, to);
                totalCandles += candles;

                var gaps = await archive.FindGapsAsync(symbol, interval, from, to);
                Console.WriteLine(gaps.Count == 0
                    ? $"{symbol} {interval}: +{candles} nến, kho liền mạch."
                    : $"{symbol} {interval}: +{candles} nến, CÒN {gaps.Count} khoảng thiếu.");
            }
        }

        timer.Stop();
        Console.WriteLine($"Hoàn tất: +{totalCandles} nến, +{totalFunding} mốc phí vốn trong {timer.Elapsed}.");

        return 0;
    }

    /// <summary><c>backtest --account 1 --symbol BTCUSDT --from ... --to ... [--name ...]</c></summary>
    private static async Task<int> BacktestAsync(string[] args, WebApplication app)
    {
        var options = Options(args);
        if (!TryOption(options, "symbols", "symbol", out var symbolsText)
            || !options.TryGetValue("from", out var fromText)
            || !options.TryGetValue("to", out var toText)
            || !TryDate(fromText, out var from)
            || !TryDate(toText, out var to)) return Usage();

        var symbols = Csv(symbolsText).Select(s => s.ToUpperInvariant()).ToList();
        if (symbols.Count == 0 || to <= from) return Usage();
        var name = options.TryGetValue("name", out var suppliedName)
            ? suppliedName
            : $"{string.Join(',', symbols)} {from:yyyy-MM-dd}..{to:yyyy-MM-dd}";

        long accountId;
        EngineSetting? settingsOverride;
        using (var lookup = app.Services.CreateScope())
        {
            var db = lookup.ServiceProvider.GetRequiredService<MmwDbContext>();
            accountId = options.TryGetValue("account", out var accountText) && long.TryParse(accountText, out var given)
                ? given
                : db.EngineSettings.OrderBy(s => s.Id).Select(s => s.TradingAccountId).FirstOrDefault();

            settingsOverride = await db.EngineSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TradingAccountId == accountId);
        }

        if (accountId == 0)
        {
            Console.Error.WriteLine("Không tìm thấy tài khoản nào có cấu hình engine.");
            return 1;
        }

        if (settingsOverride is null)
        {
            Console.Error.WriteLine($"Tài khoản {accountId} chưa có cấu hình engine.");
            return 1;
        }

        if (options.TryGetValue("version", out var versionText))
        {
            settingsOverride.StrategyVersion = versionText.ToLowerInvariant() switch
            {
                "v2" or "2" or "adaptivev2" => TradingStrategyVersion.AdaptiveV2,
                "v3" or "3" or "triggerfirstv3" => TradingStrategyVersion.TriggerFirstV3,
                "v5" or "5" or "calibratedv5" => TradingStrategyVersion.CalibratedV5,
                "v6" or "6" or "adaptivesidewaysv6" => TradingStrategyVersion.AdaptiveSidewaysV6,
                _ => throw new ArgumentException("--version chỉ nhận v2, v3, v5 hoặc v6."),
            };
        }

        if (options.TryGetValue("fill", out var fillText))
        {
            settingsOverride.BacktestLimitFillRequiresThrough = fillText.ToLowerInvariant() switch
            {
                "conservative" or "through" => true,
                "optimistic" or "touch" => false,
                _ => throw new ArgumentException("--fill chỉ nhận conservative hoặc optimistic."),
            };
        }

        var collectTelemetry = !options.TryGetValue("telemetry", out var telemetryText)
                               || !string.Equals(telemetryText, "false", StringComparison.OrdinalIgnoreCase);

        // Scope riêng với HAI cổng bị thay. Mọi service khác giữ nguyên như chạy thật —
        // đó chính là nội dung của FR-053.
        var services = new ServiceCollection();
        foreach (var descriptor in app.Services.GetRequiredService<IServiceCollectionSnapshot>().Descriptors)
            ((IList<ServiceDescriptor>)services).Add(descriptor);

        var clock = new BacktestClock(from);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(clock);
        services.AddScoped<ArchiveMarketDataProvider>();
        services.AddScoped<IMarketDataProvider>(sp => sp.GetRequiredService<ArchiveMarketDataProvider>());
        services.AddScoped<IMarketSentimentProvider>(sp => sp.GetRequiredService<ArchiveMarketDataProvider>());
        services.AddScoped<IBacktestEngine, BacktestEngine>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
        var timer = Stopwatch.StartNew();
        var report = await engine.RunAsync(new BacktestRequest(
            name, from, to, symbols, accountId, settingsOverride, CollectTelemetry: collectTelemetry));
        timer.Stop();

        Console.WriteLine($"Lần chạy #{report.RunId} [{report.StrategyVersion}]: {report.TradeCount} lệnh, " +
                          $"thắng {report.WinRate:N1}%, kỳ vọng {report.ExpectancyR:N3}R, " +
                          $"sụt giảm tối đa {report.MaxDrawdownPercent:N2}R.");
        if (report.Telemetry is { } telemetry)
        {
            Console.WriteLine(
                $"Telemetry {telemetry.SchemaVersion}: gross {telemetry.GrossExpectancyR:N4}R, " +
                $"friction {telemetry.AverageFrictionR:N4}R/lệnh, " +
                $"decision={telemetry.DecisionFingerprint[..12]}, trade={telemetry.TradeFingerprint[..12]}.");
            Console.WriteLine(
                $"Event funnel: candidate {telemetry.DistinctCandidateEventCount:N0}, " +
                $"confirmed {telemetry.DistinctConfirmedEventCount:N0}, " +
                $"entered {telemetry.DistinctEnteredEventCount:N0}.");

            if (telemetry.EntryFills is { } fills)
            {
                Console.WriteLine(
                    $"Entry fill: market {fills.MarketTranchesFilled:N0}/{fills.MarketTranchesOffered:N0} " +
                    $"({fills.MarketFillRatePercent:N1}%), limit " +
                    $"{fills.LimitTranchesFilled:N0}/{fills.LimitTranchesOffered:N0} " +
                    $"({fills.LimitFillRatePercent:N1}%), hết hạn {fills.LimitTranchesExpired:N0}.");
                Console.WriteLine("Theo trạng thái khớp entry (attribution quan sát, không phải counterfactual):");
                foreach (var (state, stats) in fills.ByFillState.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine(
                        $"  • {state}: {stats.Trades.TradeCount:N0} lệnh, " +
                        $"thắng {stats.Trades.WinRate:N1}%, net {stats.Trades.ExpectancyR:N3}R, " +
                        $"gross {stats.GrossExpectancyR:N3}R, friction {stats.AverageFrictionR:N3}R, " +
                        $"risk đã khớp {stats.AverageFilledRiskBudgetFraction:P1}.");
                }
                Console.WriteLine("Theo setup × trạng thái khớp:");
                foreach (var (group, stats) in fills.BySetupAndFillState
                             .OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine(
                        $"  • {group}: {stats.Trades.TradeCount:N0} lệnh, " +
                        $"thắng {stats.Trades.WinRate:N1}%, net {stats.Trades.ExpectancyR:N3}R, " +
                        $"gross {stats.GrossExpectancyR:N3}R, friction {stats.AverageFrictionR:N3}R.");
                }
            }
        }
        Console.WriteLine(
            $"Khoảng tin cậy 95%: win rate [{report.WinRate95?.Lower:N1}%, {report.WinRate95?.Upper:N1}%], " +
            $"expectancy [{report.ExpectancyR95?.Lower:N3}R, {report.ExpectancyR95?.Upper:N3}R]. " +
            $"Đây là lần thử so sánh #{report.ComparableTrialNumber} trên đúng khoảng và tập mã này.");
        Console.WriteLine(
            $"DirectionMargin loại thực sự {report.DirectionMarginMaterialBlocks:N0} setup đã đủ điểm " +
            $"(không tính các lượt vốn đã BelowThreshold).");

        if (report.ByMode is { Count: > 0 })
        {
            Console.WriteLine("Theo Mode:");
            foreach (var (mode, stats) in report.ByMode.OrderBy(x => x.Key, StringComparer.Ordinal))
                Console.WriteLine(
                    $"  • {mode}: {stats.TradeCount} lệnh, thắng {stats.WinRate:N1}%, " +
                    $"expectancy {stats.ExpectancyR:N3}R " +
                    $"(95% [{stats.ExpectancyR95.Lower:N3}, {stats.ExpectancyR95.Upper:N3}]R)" +
                    (stats.HasMinimumSample ? string.Empty : " — CHƯA ĐỦ 100 LỆNH"));
        }

        if (report.ByExitReason is { Count: > 0 })
        {
            Console.WriteLine("Theo ExitReason:");
            foreach (var (reason, stats) in report.ByExitReason.OrderBy(x => x.Key))
                Console.WriteLine(
                    $"  • {reason}: {stats.TradeCount} lệnh, thắng {stats.WinRate:N1}%, " +
                    $"expectancy {stats.ExpectancyR:N3}R");
        }

        if (report.StructuralRr is { } rr)
        {
            Console.WriteLine(
                $"R:R cấu trúc: đo được {rr.ObservedCount:N0}/{rr.EvaluatedCount:N0} lượt; " +
                $"stop không hợp lệ {rr.UnplannableStopCount:N0}; " +
                $"P10={rr.P10:N2}, P25={rr.P25:N2}, P50={rr.Median:N2}, " +
                $"P75={rr.P75:N2}, P90={rr.P90:N2}.");
            Console.WriteLine("Tỉ lệ giữ lại theo rào R:R (trên toàn bộ lượt đã đánh giá):");
            foreach (var threshold in rr.Thresholds)
                Console.WriteLine(
                    $"  • ≥ {threshold.ThresholdR:N2}R: {threshold.EligibleCount:N0} " +
                    $"({threshold.PercentOfEvaluated:N1}%)");
        }

        // Chi phí trung bình mỗi lệnh, quy ra R. Đây là đầu vào để tính lại tỉ lệ thắng hoà vốn
        // từ SỐ ĐO thật thay vì từ tỉ lệ phí giả định — thứ đã khiến §0 của tài liệu V2 ước
        // lượng lệch bảy điểm phần trăm.
        if (report.TradeCount > 0)
        {
            var n = report.TradeCount;
            var totalCost = report.TotalFeeR + report.TotalFundingR + report.TotalSlippageR;
            Console.WriteLine(
                $"Chi phí mỗi lệnh: phí giao dịch {report.TotalFeeR / n:N4}R, " +
                $"phí vốn {report.TotalFundingR / n:N4}R, " +
                $"trượt giá {report.TotalSlippageR / n:N4}R " +
                $"(tổng friction {totalCost / n:N4}R/lệnh, {totalCost:N2}R toàn kỳ).");

            if (report.LimitTranchesOffered > 0)
            {
                Console.WriteLine(
                    $"Chân limit: khớp {report.LimitFillRatePercent:N1}%, " +
                    $"hết hạn {report.LimitExpiryRatePercent:N1}%; " +
                    $"phí trả theo biểu maker {report.MakerFeeSharePercent:N1}%.");
            }
        }

        if (options.TryGetValue("dump", out var dumpPath)
            && report.Telemetry?.TradeRows is { Count: > 0 } rows)
        {
            WriteTradeCsv(dumpPath, rows);
            Console.WriteLine($"Đã ghi {rows.Count:N0} dòng lệnh vào {dumpPath}.");
        }

        Console.WriteLine();
        Console.WriteLine("HẠN CHẾ — đọc trước khi tin các con số:");
        foreach (var line in report.Limitations) Console.WriteLine($"  • {line}");
        Console.WriteLine($"Thời gian chạy: {timer.Elapsed}.");

        return 0;
    }

    /// <summary>
    /// Xuất một dòng cho mỗi lệnh để phân tích ngoại tuyến. Chỉ là quan sát: cột được lấy từ
    /// telemetry đã dựng xong, sau khi mọi quyết định đã chốt.
    /// </summary>
    private static void WriteTradeCsv(string path, IReadOnlyList<TelemetryTradeRow> rows)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var csv = new StringBuilder(rows.Count * 200);
        csv.AppendLine(
            "Symbol,OrderPlacedAtUtc,Direction,Setup,Trigger,Mode,Regime,Score,ExpectedCostR," +
            "NetRiskReward,RiskReward,StopDistanceBps,PlannedSizeR,CriterionPoints,FillState,RMultiple,FeeR," +
            "FundingR,SlippageR,FilledRiskBudgetR,ActualCostR,FundingSettlements,BarsHeld," +
            "MfeR,MaeR,ExitReason,Outcome");

        foreach (var r in rows)
        {
            csv.Append(r.Symbol).Append(',')
                .Append(r.OrderPlacedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Direction).Append(',')
                .Append(r.Setup).Append(',')
                .Append(r.Trigger).Append(',')
                .Append(r.Mode).Append(',')
                .Append(r.Regime).Append(',')
                .Append(r.Score.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(N(r.ExpectedCostR)).Append(',')
                .Append(N(r.NetRiskReward)).Append(',')
                .Append(N(r.RiskReward)).Append(',')
                .Append(N(r.StopDistanceBps)).Append(',')
                .Append(N(r.PlannedSizeR)).Append(',')
                .Append(r.CriterionPoints).Append(',')
                .Append(r.FillState).Append(',')
                .Append(N(r.RMultiple)).Append(',')
                .Append(N(r.FeeR)).Append(',')
                .Append(N(r.FundingR)).Append(',')
                .Append(N(r.SlippageR)).Append(',')
                .Append(N(r.FilledRiskBudgetR)).Append(',')
                .Append(N(r.ActualCostR)).Append(',')
                .Append(r.FundingSettlements.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.BarsHeld.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(N(r.MfeR)).Append(',')
                .Append(N(r.MaeR)).Append(',')
                .Append(r.ExitReason).Append(',')
                .Append(r.Outcome).AppendLine();
        }

        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));

        static string N(decimal? value) =>
            value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryDate(string value, out DateTime utc)
    {
        var ok = DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return ok;
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) continue;
            result[args[i][2..]] = args[++i];
        }
        return result;
    }

    private static bool TryOption(
        IReadOnlyDictionary<string, string> options, string plural, string singular, out string value) =>
        options.TryGetValue(plural, out value!) || options.TryGetValue(singular, out value!);

    private static IEnumerable<string> Csv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);


    /// <summary>
    /// <c>ordertest --account ID</c> — gửi mọi tổ hợp mã × kiểu lệnh vào endpoint KIỂM TRA của sàn.
    /// </summary>
    /// <remarks>
    /// Tiền kiểm này tồn tại vì ngày 18/08/2026 bốn lệnh đầu tiên của hệ thống bị bác -1111: bộ
    /// lọc precision lấy nhầm mã (exchangeInfo bỏ qua ?symbol= nên luôn trả BTCUSDT). Lỗi loại đó
    /// chỉ lộ ra ở đúng khoảnh khắc đặt lệnh thật, mà khoảnh khắc ấy không lặp lại theo ý muốn.
    ///
    /// Khối lượng cố ý để LẺ (0.123456789) đúng như engine sinh ra, để bài kiểm thật sự kiểm
    /// phần làm tròn chứ không kiểm một con số đã sạch sẵn.
    /// </remarks>
    private static async Task<int> OrderTestAsync(string[] args, WebApplication app)
    {
        var options = Options(args);
        if (!options.TryGetValue("account", out var accountText)
            || !long.TryParse(accountText, out var accountId)) return Usage();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IExchangeOrderProviderFactory>();
        var live = scope.ServiceProvider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<LiveTradingOptions>>().Value;

        var account = await db.TradingAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
        {
            Console.Error.WriteLine($"Tài khoản {accountId} không tồn tại hoặc thiếu API key.");
            return 1;
        }

        var provider = factory.Create(account.ApiKey!, account.ApiSecret!, live.UseTestnet);
        Console.WriteLine($"Tiền kiểm lệnh — tài khoản {accountId}, testnet={live.UseTestnet}");

        var failures = 0;
        foreach (var symbol in new[] { "BTCUSDT", "ETHUSDT" })
        {
            var price = await ReferencePriceAsync(scope, symbol);
            if (price <= 0m) { Console.WriteLine($"  {symbol}: bỏ qua, không lấy được giá."); continue; }

            foreach (var (label, req) in Probes(symbol, price))
            {
                try
                {
                    var result = await provider.ValidateFuturesOrderAsync(req);
                    Console.WriteLine($"  ✓ {symbol,-9} {label,-22} {result}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"  ✗ {symbol,-9} {label,-22} {ex.Message}");
                }
            }
        }

        Console.WriteLine(failures == 0
            ? "Tất cả tổ hợp đều được sàn chấp nhận."
            : $"CÓ {failures} tổ hợp bị sàn từ chối — xem ở trên.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<decimal> ReferencePriceAsync(IServiceScope scope, string symbol)
    {
        try
        {
            var md = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
            return (await md.GetTickerAsync(symbol)).Price;
        }
        catch { return 0m; }
    }

    /// <summary>Các tổ hợp đáng kiểm. Giá lệnh chờ đặt XA thị trường để GTX chắc chắn không cắt sổ.</summary>
    private static IEnumerable<(string Label, FuturesOrderRequest Req)> Probes(string symbol, decimal price)
    {
        const decimal oddQty = 0.123456789m;

        yield return ("MARKET mua", new FuturesOrderRequest
        {
            Symbol = symbol, Side = OrderSide.Buy, Kind = FuturesOrderKind.Market,
            Quantity = oddQty, PositionSide = FuturesPositionSide.Long,
        });

        yield return ("LIMIT GTX mua", new FuturesOrderRequest
        {
            Symbol = symbol, Side = OrderSide.Buy, Kind = FuturesOrderKind.Limit,
            Quantity = oddQty, Price = price * 0.90m, TimeInForce = "GTX",
            PositionSide = FuturesPositionSide.Long,
        });

        yield return ("LIMIT GTX bán", new FuturesOrderRequest
        {
            Symbol = symbol, Side = OrderSide.Sell, Kind = FuturesOrderKind.Limit,
            Quantity = oddQty, Price = price * 1.10m, TimeInForce = "GTX",
            PositionSide = FuturesPositionSide.Short,
        });

        yield return ("STOP_MARKET đóng", new FuturesOrderRequest
        {
            Symbol = symbol, Side = OrderSide.Sell, Kind = FuturesOrderKind.StopMarket,
            StopPrice = price * 0.95m, ClosePosition = true, PositionSide = FuturesPositionSide.Long,
        });
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Cách dùng:
              backfill --symbols LIST --intervals LIST --from DATE [--to DATE]
              ordertest --account ID
              backtest --account ID --symbol SYMBOL --from DATE --to DATE [--version v2|v3|v5|v6]
                       [--fill conservative|optimistic] [--telemetry true|false] [--name NAME]
                       [--dump FILE.csv]

            Ví dụ:
              backfill --symbols BTCUSDT,ETHUSDT --intervals 15m,4h,1d --from 2024-01-01 --to 2026-01-01
              backtest --account 1 --symbols BTCUSDT,ETHUSDT --from 2020-01-01 --to 2026-08-04 --version v3
            """);
        return 1;
    }
}

/// <summary>Giữ lại bản mô tả dịch vụ gốc để dựng được scope kiểm thử.</summary>
/// <remarks>
/// <c>IServiceProvider</c> đã dựng thì không liệt kê lại được các đăng ký của nó, nên phải
/// chụp <c>IServiceCollection</c> lúc khởi động.
/// </remarks>
public interface IServiceCollectionSnapshot
{
    IReadOnlyList<ServiceDescriptor> Descriptors { get; }
}

public sealed class ServiceCollectionSnapshot : IServiceCollectionSnapshot
{
    public ServiceCollectionSnapshot(IServiceCollection services) => Descriptors = services.ToList();

    public IReadOnlyList<ServiceDescriptor> Descriptors { get; }
}
