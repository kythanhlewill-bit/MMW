using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

public interface IDayRegimeClassifier
{
    /// <summary>Hàm THUẦN. Không I/O, không đồng hồ. Toàn bộ đầu vào nằm trong inputs.</summary>
    RegimeClassification Classify(DailyPlanInputs inputs, EngineSetting settings);
}

/// <summary>
/// Năm bước phân loại ngày theo contracts/daily-plan.md.
/// </summary>
/// <remarks>
/// KHÔNG BAO GIỜ ném vì thiếu dữ liệu (bất biến 6). Một ngoại lệ ở đây làm job kế hoạch ngày
/// chết, để hệ thống không có kế hoạch; mà theo FR-023, không có kế hoạch nghĩa là CẢ NGÀY
/// không giao dịch được. Suy biến an toàn nhưng không mong muốn — nên tránh bằng cách trả về
/// một kế hoạch thận trọng thay vì đổ vỡ.
/// </remarks>
public sealed class DayRegimeClassifier : IDayRegimeClassifier
{
    /// <summary>Số phiên dùng để đọc cấu trúc giá (FR-017).</summary>
    private const int StructureLookbackDays = 20;

    /// <summary>Số phiên dùng làm nền so sánh phân vị biến động (FR-017).</summary>
    private const int VolatilityLookbackDays = 90;

    private const int AtrPeriod = 14;

    /// <summary>Trần hệ số rủi ro khi thiếu bất kỳ đầu vào nào (FR-022, bước 5).</summary>
    private const decimal MissingInputRiskCap = 0.5m;

    private readonly ISwingDetector _swings;
    private readonly IIndicatorService _indicators;

    public DayRegimeClassifier(ISwingDetector swings, IIndicatorService indicators)
    {
        _swings = swings;
        _indicators = indicators;
    }

    public RegimeClassification Classify(DailyPlanInputs inputs, EngineSetting settings)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(settings);

        var missing = new List<string>();

        // ── Bước 1 — cấu trúc giá ───────────────────────────────────────
        var structure = ReadStructure(inputs.BtcDailyCandles, settings.SwingPivotBars, missing);

        // ── Bước 2 — chế độ biến động ───────────────────────────────────
        var atrPercentile = ReadAtrPercentile(inputs.BtcDailyCandles, missing);
        var volatility = VolatilityBands.From(atrPercentile);

        // ── Bước 3 + 4 — bảng FR-019 và hợp nhất FR-020 ─────────────────
        var hasHighImpactEvent = inputs.TodayEvents.Any(e => e.Impact >= MacroEventImpact.High);
        var parameters = RegimeTable.Resolve(structure, volatility, hasHighImpactEvent);

        // ── Bước 5 — phạt khi thiếu dữ liệu ─────────────────────────────
        CollectMissingOptionalInputs(inputs, missing);

        var risk = missing.Count > 0
            ? Math.Min(parameters.RiskMultiplier, MissingInputRiskCap)   // TRẦN, không phải giá trị cố định
            : parameters.RiskMultiplier;

        return new RegimeClassification(
            Regime: Label(structure, volatility, hasHighImpactEvent),
            Volatility: volatility,
            AllowedDirections: parameters.AllowedDirections,
            RiskMultiplier: risk,
            MaxTradesToday: parameters.MaxTradesToday,
            BtcStructure: structure.ToString(),
            AtrPercentile: atrPercentile,
            MissingInputs: missing);
    }

    /// <summary>
    /// Nhãn tổng của ngày. Biến động cực đoan nặng hơn ngày có tin (hệ số 0.3 so với 0.4)
    /// nên nó thắng khi cả hai cùng đúng.
    /// </summary>
    /// <remarks>
    /// Ngưỡng là <see cref="VolatilityRegime.High"/> chứ không phải <c>Extreme</c>. Trước đây một
    /// ngày ở phân vị 88 được dán nhãn TrendUp/TrendDown/Range, nên
    /// <c>market.day_regime_match</c> vẫn có thể cho 10/10 cho một lệnh thuận xu hướng — trong
    /// khi đó đúng là loại ngày mà dừng lỗ bị quét nhiều nhất. Nhãn phải nói thật về ngày đó.
    /// </remarks>
    private static DayRegime Label(DayStructure structure, VolatilityRegime volatility, bool hasHighImpactEvent)
    {
        if (volatility >= VolatilityRegime.High) return DayRegime.HighVolatility;
        if (hasHighImpactEvent) return DayRegime.EventDay;

        return structure switch
        {
            DayStructure.TrendUp => DayRegime.TrendUp,
            DayStructure.TrendDown => DayRegime.TrendDown,
            _ => DayRegime.Range,
        };
    }

    /// <summary>
    /// Bước 1: đỉnh xoay cuối so với đỉnh trước, đáy xoay cuối so với đáy trước.
    /// </summary>
    /// <remarks>
    /// Cần CẢ HAI cùng cao dần mới là xu hướng tăng. Chỉ đỉnh cao dần mà đáy thấp dần là biên
    /// độ đang mở rộng — một trạng thái khác hẳn, và gọi nó là xu hướng tăng sẽ cho phép mua
    /// đúng vào lúc thị trường mất phương hướng.
    /// </remarks>
    private DayStructure ReadStructure(IReadOnlyList<Candle> candles, int pivotBars, List<string> missing)
    {
        if (candles.Count < StructureLookbackDays)
        {
            missing.Add(DailyPlanInputNames.BtcStructure);
            return DayStructure.Range;
        }

        var window = candles.Skip(candles.Count - StructureLookbackDays).ToList();
        var pivots = _swings.Detect(window, Math.Max(1, pivotBars));

        var highs = pivots.Where(p => p.IsHigh).ToList();
        var lows = pivots.Where(p => !p.IsHigh).ToList();

        if (highs.Count < 2 || lows.Count < 2)
        {
            missing.Add(DailyPlanInputNames.BtcStructure);
            return DayStructure.Range;
        }

        var higherHigh = highs[^1].Price > highs[^2].Price;
        var higherLow = lows[^1].Price > lows[^2].Price;
        var lowerHigh = highs[^1].Price < highs[^2].Price;
        var lowerLow = lows[^1].Price < lows[^2].Price;

        if (higherHigh && higherLow) return DayStructure.TrendUp;
        if (lowerHigh && lowerLow) return DayStructure.TrendDown;
        return DayStructure.Range;
    }

    /// <summary>
    /// Bước 2: phân vị của <c>ATR(14) / giá đóng cửa</c> hiện tại so với tối đa 90 phiên gần nhất.
    /// </summary>
    /// <remarks>
    /// Chuỗi lịch sử dựng bằng cách cắt PHẦN ĐUÔI chuỗi nến rồi tính lại. Vì làm trơn Wilder là
    /// một hồi quy khởi tạo ở đầu chuỗi, cắt đuôi không đụng vào phần đầu — nên giá trị tính
    /// được ở mỗi vị trí đúng bằng giá trị của một phép tính cuộn liên tục.
    /// </remarks>
    private decimal? ReadAtrPercentile(IReadOnlyList<Candle> candles, List<string> missing)
    {
        var series = BuildAtrPercentSeries(candles);

        if (series.Count == 0)
        {
            missing.Add(DailyPlanInputNames.AtrPercentile);
            return null;
        }

        var percentile = _indicators.PercentileOf(series, series[^1]);
        if (percentile is null) missing.Add(DailyPlanInputNames.AtrPercentile);

        return percentile;
    }

    private List<decimal> BuildAtrPercentSeries(IReadOnlyList<Candle> candles)
    {
        var series = new List<decimal>();
        if (candles.Count <= AtrPeriod) return series;

        var first = Math.Max(AtrPeriod, candles.Count - VolatilityLookbackDays);

        for (var end = first; end < candles.Count; end++)
        {
            var close = candles[end].Close;
            if (close <= 0m) continue;   // giá 0 không chia được; bỏ mẫu chứ không ném

            var atr = _indicators.Atr(candles.Take(end + 1).ToList(), AtrPeriod);
            if (atr is null) continue;

            series.Add(atr.Value / close * 100m);
        }

        return series;
    }

    private static void CollectMissingOptionalInputs(DailyPlanInputs inputs, List<string> missing)
    {
        if (inputs.SymbolDailyCandles.Count == 0) missing.Add(DailyPlanInputNames.KeyLevels);
        if (inputs.FundingRate is null) missing.Add(DailyPlanInputNames.FundingRate);
        if (inputs.OpenInterestChange24hPercent is null) missing.Add(DailyPlanInputNames.OpenInterestChange);
        if (inputs.LongShortAccountRatio is null) missing.Add(DailyPlanInputNames.LongShortRatio);
        if (inputs.FearGreedIndex is null) missing.Add(DailyPlanInputNames.FearGreed);
    }
}
