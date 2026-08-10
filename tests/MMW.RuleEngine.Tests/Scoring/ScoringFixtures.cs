using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Structure;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// Bộ dựng dữ liệu cho các test chấm điểm.
/// </summary>
/// <remarks>
/// Nguyên tắc dựng: mỗi hàm điều khiển ĐÚNG một tính chất của chuỗi giá, và những tính chất
/// khó ghim bằng chuỗi giá (dải RSI, ngưỡng khối lượng, phí vốn cực đoan) thì điều khiển bằng
/// CẤU HÌNH thay vì bằng số liệu. Ghim gián tiếp qua chuỗi giá là làm được nhưng mong manh:
/// test sẽ đỏ vì chuỗi lệch một chút chứ không phải vì tiêu chí sai.
/// </remarks>
internal static class ScoringFixtures
{
    public static readonly DateTime Now = new(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
    public const string Symbol = "ETHUSDT";
    public const string Leader = "BTCUSDT";

    public static readonly IIndicatorService Indicators = new IndicatorService();
    public static readonly ISwingDetector Swings = new SwingDetector();
    public static readonly MarketStructureAnalyzer Structure = new(Swings);

    private static Candle Bar(int i, decimal close, decimal range, decimal volume, TimeSpan step) => new(
        OpenTime: Now.AddMinutes(-(1000 - i) * step.TotalMinutes),
        Open: close,
        High: close + range / 2m,
        Low: close - range / 2m,
        Close: close,
        Volume: volume,
        CloseTime: Now.AddMinutes(-(999 - i) * step.TotalMinutes).AddTicks(-1));

    /// <summary>Chuỗi tăng đều tuyến tính. Chồng EMA xếp tăng.</summary>
    public static List<Candle> Ramp(int count, decimal start = 1000m, decimal step = 1m,
        decimal range = 2m, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        return Enumerable.Range(0, count).Select(i => Bar(i, start + i * step, range, volume, iv)).ToList();
    }

    /// <summary>
    /// Chuỗi tăng có GIA TỐC. Khác chuỗi tuyến tính ở chỗ biểu đồ MACD tiếp tục dốc lên thay vì
    /// hội tụ về 0 — cần thiết để ghim ca "động lượng thuận" của tiêu chí momentum.
    /// </summary>
    public static List<Candle> Accelerating(int count, decimal start = 1000m, decimal k = 0.01m,
        decimal range = 2m, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        return Enumerable.Range(0, count).Select(i => Bar(i, start + k * i * i, range, volume, iv)).ToList();
    }

    /// <summary>Ảnh gương của <see cref="Accelerating"/>: giảm có gia tốc.</summary>
    public static List<Candle> Decelerating(int count, decimal start = 1000m, decimal k = 0.01m,
        decimal range = 2m, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        return Enumerable.Range(0, count).Select(i => Bar(i, start - k * i * i, range, volume, iv)).ToList();
    }

    /// <summary>
    /// Chuỗi răng cưa có ĐỈNH VÀ ĐÁY XOAY thật, đi lên dần.
    /// </summary>
    /// <remarks>
    /// Cần thiết cho mọi tiêu chí đọc điểm xoay. Chuỗi tăng đơn điệu KHÔNG có điểm xoay fractal
    /// nào — mỗi nến đều cao hơn nến trước nhưng thấp hơn nến sau, nên không nến nào là cực trị
    /// địa phương. Dùng chuỗi đơn điệu ở đó thì tiêu chí trả "thiếu dữ liệu" chứ không trả điểm,
    /// và test sẽ đo nhầm thứ mình định đo.
    ///
    /// Chu kỳ 8 nến với <c>SwingPivotBars = 2</c> cho đúng một đỉnh và một đáy mỗi chu kỳ.
    ///
    /// Độ trôi mỗi chu kỳ phải KHÁC bước răng cưa (<c>0.25 × amplitude</c>). Bằng nhau thì hai
    /// nến hai bên đáy có giá y hệt nhau, và bộ phát hiện điểm xoay dùng so sánh CHẶT nên đáy
    /// đó biến mất — chuỗi trông có răng cưa nhưng không sinh điểm xoay đáy nào.
    /// </remarks>
    public static List<Candle> ZigZag(int count, decimal start = 1000m, decimal amplitude = 20m,
        decimal driftPerCycle = 4m, decimal range = 2m, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        decimal[] shape = { 0m, 0.25m, 0.5m, 0.75m, 1m, 0.75m, 0.5m, 0.25m };

        return Enumerable.Range(0, count)
            .Select(i => Bar(i, start + i / shape.Length * driftPerCycle + shape[i % shape.Length] * amplitude,
                range, volume, iv))
            .ToList();
    }

    /// <summary>Dựng nến từ một đường giá cho sẵn, biên độ ±1 quanh mỗi mức.</summary>
    public static List<Candle> FromPath(IReadOnlyList<decimal> path, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        return path.Select((p, i) => Bar(i, p, 2m, volume, iv)).ToList();
    }

    /// <summary>
    /// Đường giá có phá vỡ cấu trúc RỒI KIỂM ĐỊNH LẠI THÀNH CÔNG.
    /// </summary>
    /// <remarks>
    /// Chuỗi răng cưa đều không dựng được ca này: nó phá đỉnh rồi rơi ngay xuống dưới ở nhịp
    /// sau, nên luôn cho ra "kiểm định lại thất bại". Ca thành công cần một hình dạng riêng —
    /// tạo đỉnh xoay, vượt qua dứt khoát, lùi về chạm đúng mức đã phá rồi đóng cửa trở lại
    /// phía trên.
    ///
    /// Chỉ được có ĐÚNG MỘT đỉnh xoay, ở nến 2 (giá cao nhất 106). Nếu đoạn bò lên cũng dao
    /// động thì nó sinh thêm một đỉnh xoay gần hơn, và bộ phân tích sẽ lấy mức của đỉnh gần đó
    /// làm mức phá vỡ — cú vượt 106 mà test định dựng biến mất khỏi phép đo. Vì vậy đoạn 5–17
    /// đi lên đơn điệu: chuỗi đơn điệu không sinh điểm xoay nào.
    ///
    /// Nến 18 đóng ở 107 trong khi nến 17 đóng đúng 106 — đó là lần VƯỢT QUA. Nến 19 lùi về
    /// chạm 105.2 rồi nến 20 đóng hẳn trên dải, tức đã kiểm định lại thành công.
    /// </remarks>
    public static List<Candle> BreakoutWithRetest() => FromPath(new[]
    {
        100m, 101m, 105m, 101m, 100m,                     // 0–4:   đỉnh xoay duy nhất tại nến 2
         99m, 99.5m, 100m, 100.5m, 101m,                  // 5–9:   bò lên đơn điệu, không sinh đỉnh
        101.5m, 102m, 102.5m, 103m, 103.5m,               // 10–14
        104m, 105m, 106m, 107m,                           // 15–18: nến 18 vượt qua mức 106
        106.2m,                                           // 19:    lùi về chạm lại mức đã phá
        108m, 109m, 110m, 111m, 112m, 113m, 114m,         // 20–26: đóng hẳn trở lại phía trên
    });

    public static List<Candle> Flat(int count, decimal price = 1000m,
        decimal range = 2m, decimal volume = 100m, TimeSpan? interval = null)
    {
        var iv = interval ?? TimeSpan.FromMinutes(15);
        return Enumerable.Range(0, count).Select(i => Bar(i, price, range, volume, iv)).ToList();
    }

    public static EngineSetting Settings(Action<EngineSetting>? configure = null)
    {
        var s = EngineSettingDefaults.Create(tradingAccountId: 1);
        configure?.Invoke(s);
        return s;
    }

    public static DailyPlan Plan(
        AllowedDirections directions = AllowedDirections.Both,
        DayRegime regime = DayRegime.Range,
        VolatilityRegime volatility = VolatilityRegime.Normal,
        decimal? atrPercentile = 50m,
        decimal riskMultiplier = 1.0m,
        int maxTrades = 5) => new()
        {
            Id = 1,
            TradingAccountId = 1,
            PlanDateUtc = DateOnly.FromDateTime(Now),
            GeneratedAtUtc = Now.AddHours(-14),
            DayRegime = regime,
            VolatilityRegime = volatility,
            AllowedDirections = directions,
            RiskMultiplier = riskMultiplier,
            MaxTradesToday = maxTrades,
            AtrPercentile = atrPercentile,
            IsComplete = true,
        };

    public static DepthSnapshot Depth(decimal spreadBps, decimal mid = 1000m)
    {
        var half = mid * spreadBps / 10_000m / 2m;
        return new DepthSnapshot(
            new[] { new DepthLevel(mid - half, 100m), new DepthLevel(mid - half * 3, 100m) },
            new[] { new DepthLevel(mid + half, 100m), new DepthLevel(mid + half * 3, 100m) },
            Now);
    }

    public static OpenInterestSeries OpenInterest(decimal changePercent)
    {
        var start = 1_000_000m;
        var end = start * (1m + changePercent / 100m);
        return new OpenInterestSeries(Symbol, "1h", new[]
        {
            new OpenInterestPoint(Now.AddHours(-5), start, start),
            new OpenInterestPoint(Now, end, end),
        });
    }

    public static FundingSnapshot Funding(decimal rate) => new(rate, Now.AddHours(1), 1000m, Now);

    /// <summary>
    /// Tín hiệu price action bơm thẳng vào bối cảnh, tính bằng TUỔI (số nến kể từ lúc hoàn thành).
    /// </summary>
    /// <remarks>
    /// Dựng một chuỗi giá có đúng mẫu hình ở đúng tuổi là làm được nhưng mong manh: test sẽ đỏ vì
    /// chuỗi lệch một nến chứ không phải vì tiêu chí sai. Việc NHẬN DIỆN mẫu hình đã có bộ test
    /// riêng của <c>PriceActionAnalyzer</c>; ở đây chỉ đo cách tiêu chí phản ứng với tín hiệu.
    /// </remarks>
    public static PriceActionSignals PriceAction(
        int? doubleBottom = null,
        int? doubleTop = null,
        int? bullishRsiDivergence = null,
        int? bearishRsiDivergence = null,
        bool fibonacciLong = false,
        bool fibonacciShort = false) => new(
        BullishStaircase: null,
        BearishStaircase: null,
        DoubleBottom: doubleBottom,
        DoubleTop: doubleTop,
        InverseHeadAndShoulders: null,
        HeadAndShoulders: null,
        BullishRsiDivergence: bullishRsiDivergence,
        BearishRsiDivergence: bearishRsiDivergence,
        FibonacciLong: fibonacciLong,
        FibonacciShort: fibonacciShort);

    /// <summary>
    /// Bối cảnh mặc định: đủ dữ liệu, mọi nguồn khả dụng, kế hoạch ngày cho cả hai chiều.
    /// Test chỉnh đúng thứ nó quan tâm bằng cú pháp <c>with</c>.
    /// </summary>
    public static ScoringContext Context(
        IReadOnlyList<Candle>? entry = null,
        IReadOnlyList<Candle>? bias = null,
        TradeDirection direction = TradeDirection.Long,
        DailyPlan? plan = null,
        EngineSetting? settings = null,
        string symbol = Symbol)
    {
        var entryCandles = entry ?? Ramp(260);
        var price = entryCandles.Count > 0 ? entryCandles[^1].Close : 1000m;

        return new ScoringContext
        {
            Symbol = symbol,
            EvaluatedAtUtc = Now,
            CandleCloseTimeUtc = entryCandles.Count > 0 ? entryCandles[^1].CloseTime : Now,
            Direction = direction,
            EntryCandles = entryCandles,
            BiasCandles = bias ?? Ramp(260, interval: TimeSpan.FromHours(4)),
            DailyCandles = Ramp(120, interval: TimeSpan.FromDays(1)),
            CurrentPrice = price,
            DailyPlan = plan ?? Plan(),
            Settings = settings ?? Settings(),
            TraderStats = TraderStatistics.Empty,
            ActiveAiContext = Array.Empty<MarketContextRecord>(),
            Funding = Funding(0.0001m),
            OpenInterest = OpenInterest(5m),
            Depth = Depth(1m, price),
            LongShort = new LongShortRatio(1.1m, 0.52m, 0.48m, Now),
            LeaderCorrelation = 0.9m,
            SessionQuality = new SessionQuality(6, "Chồng lấn New York", false, 0),
            PlannedStopLoss = price * 0.98m,
            PlannedTakeProfit = price * 1.04m,

            // R:R = 2,0 — trên ngưỡng MinStructuralRr mặc định 1,6, nên `technical.structural_room`
            // cho qua và các test khác đo được đúng thứ chúng quan tâm. Test nào muốn ghim chính
            // rào này thì tự đặt lại bằng cú pháp `with`.
            StructuralLevels = new StructuralLevels(
                StopLoss: price * 0.98m,
                TakeProfit: price * 1.04m,
                RiskReward: 2.0m,
                StopIsStructural: true,
                TargetIsStructural: true,
                StopAtrMultiple: 2.0m,
                ReasonVi: "Fixture: dừng lỗ và mục tiêu theo cấu trúc, R:R 2,00."),
        };
    }

    /// <summary>Bối cảnh không có nguồn dữ liệu tuỳ chọn nào — dùng cho test FR-006.</summary>
    public static ScoringContext Starved(ScoringContext? from = null) => (from ?? Context()) with
    {
        EntryCandles = Array.Empty<Candle>(),
        BiasCandles = Array.Empty<Candle>(),
        DailyCandles = Array.Empty<Candle>(),
        Funding = null,
        OpenInterest = null,
        Depth = null,
        LongShort = null,
        LeaderCorrelation = null,
        SessionQuality = null,
        PlannedStopLoss = null,
        PlannedTakeProfit = null,
        StructuralLevels = null,
        DailyPlan = Plan(atrPercentile: null),
    };

    /// <summary>Toàn bộ 14 tiêu chí, đúng bộ mà DI đăng ký.</summary>
    public static IReadOnlyList<IScoreCriterion> AllCriteria() => new IScoreCriterion[]
    {
        new Application.Trading.Scoring.Criteria.HtfAlignmentCriterion(Indicators),
        new Application.Trading.Scoring.Criteria.MarketStructureCriterion(Structure, Indicators),
        new Application.Trading.Scoring.Criteria.EntryLocationCriterion(Indicators),
        new Application.Trading.Scoring.Criteria.MomentumCriterion(Indicators),
        new Application.Trading.Scoring.Criteria.VolumeConfirmationCriterion(Indicators),
        new Application.Trading.Scoring.Criteria.DayRegimeMatchCriterion(),
        new Application.Trading.Scoring.Criteria.VolatilityRegimeCriterion(),
        new Application.Trading.Scoring.Criteria.SessionQualityCriterion(),
        new Application.Trading.Scoring.Criteria.LeaderCorrelationCriterion(),
        new Application.Trading.Scoring.Criteria.FundingCrowdingCriterion(),
        new Application.Trading.Scoring.Criteria.OpenInterestCriterion(),
        new Application.Trading.Scoring.Criteria.LiquidityZoneCriterion(Swings),
        new Application.Trading.Scoring.Criteria.SpreadDepthCriterion(),
        new Application.Trading.Scoring.Criteria.StructuralRoomCriterion(),
    };
}
