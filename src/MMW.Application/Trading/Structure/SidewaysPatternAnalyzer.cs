using System.Globalization;
using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;

namespace MMW.Application.Trading.Structure;

public enum SidewaysPatternKind
{
    Rectangle = 1,
    Triangle = 2,
}

/// <summary>Một vùng sideway có hình học đo được, chỉ dựng từ nến đứng trước event.</summary>
public sealed record SidewaysPattern(
    SidewaysPatternKind Kind,
    int StartIndex,
    int EndIndex,
    decimal FloorAtEnd,
    decimal UpperAtEnd,
    decimal InitialWidth,
    decimal EndWidth,
    int FloorTouches,
    int UpperTouches,
    decimal ContainmentPercent,
    int GeometryQuality,
    string EventKey)
{
    public decimal Midpoint => (FloorAtEnd + UpperAtEnd) / 2m;
}

public interface ISidewaysPatternAnalyzer
{
    /// <summary>
    /// Dựng pattern từ đoạn kết thúc ngay trước <paramref name="endExclusive"/>. Caller truyền
    /// index của nến sweep/breakout để detector không bao giờ đưa chính event vào đường biên.
    /// </summary>
    SidewaysPattern? Detect(
        IReadOnlyList<Candle> candles,
        int endExclusive,
        EngineSetting settings,
        decimal atr,
        SidewaysPatternKind? requiredKind = null);
}

/// <summary>
/// Detector rectangle/triangle tất định. Dùng phân vị cho rectangle để một râu sweep đơn lẻ không
/// kéo méo biên; dùng regression trên pivot đã xác nhận cho triangle để không nhìn trước tương lai.
/// </summary>
public sealed class SidewaysPatternAnalyzer : ISidewaysPatternAnalyzer
{
    private readonly ISwingDetector _swings;

    public SidewaysPatternAnalyzer(ISwingDetector swings) => _swings = swings;

    public SidewaysPattern? Detect(
        IReadOnlyList<Candle> candles,
        int endExclusive,
        EngineSetting settings,
        decimal atr,
        SidewaysPatternKind? requiredKind = null)
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentNullException.ThrowIfNull(settings);
        if (atr <= 0m || endExclusive <= 0 || endExclusive > candles.Count) return null;

        var lookback = settings.V6PatternLookbackBars;
        if (endExclusive < lookback) return null;
        var start = endExclusive - lookback;
        var window = candles.Skip(start).Take(lookback).ToList();

        var triangle = requiredKind is null or SidewaysPatternKind.Triangle
            ? Triangle(window, start, settings, atr)
            : null;
        var rectangle = requiredKind is null or SidewaysPatternKind.Rectangle
            ? Rectangle(window, start, settings, atr)
            : null;

        if (requiredKind == SidewaysPatternKind.Triangle) return triangle;
        if (requiredKind == SidewaysPatternKind.Rectangle) return rectangle;

        // Khi cả hai cùng hợp lệ, ưu tiên tam giác chỉ nếu co hẹp đủ rõ; còn lại gọi là rectangle.
        if (triangle is not null && rectangle is not null)
            return triangle.GeometryQuality >= rectangle.GeometryQuality + 5 ? triangle : rectangle;
        return triangle ?? rectangle;
    }

    private static SidewaysPattern? Rectangle(
        IReadOnlyList<Candle> window, int globalStart, EngineSetting settings, decimal atr)
    {
        var floor = Quantile(window.Select(c => c.Low), 0.15m);
        var upper = Quantile(window.Select(c => c.High), 0.85m);
        var width = upper - floor;
        if (width <= 0m) return null;

        var widthAtr = width / atr;
        if (widthAtr < settings.V6RectangleMinWidthAtr || widthAtr > settings.V6RectangleMaxWidthAtr)
            return null;

        var tolerance = Math.Max(atr * 0.20m, width * 0.05m);
        var floorTouches = window.Count(c => c.Low <= floor + tolerance);
        var upperTouches = window.Count(c => c.High >= upper - tolerance);
        if (floorTouches < settings.V6PatternMinTouchesPerSide
            || upperTouches < settings.V6PatternMinTouchesPerSide)
            return null;

        var contained = window.Count(c => c.Close >= floor - tolerance && c.Close <= upper + tolerance);
        var containment = (decimal)contained / window.Count * 100m;
        if (containment < settings.V6PatternContainmentPercent) return null;

        var half = window.Count / 2;
        var first = window.Take(half).ToList();
        var second = window.Skip(half).ToList();
        var lowerDrift = Math.Abs(Quantile(second.Select(c => c.Low), 0.15m)
                                  - Quantile(first.Select(c => c.Low), 0.15m));
        var upperDrift = Math.Abs(Quantile(second.Select(c => c.High), 0.85m)
                                  - Quantile(first.Select(c => c.High), 0.85m));
        var driftAtr = Math.Max(lowerDrift, upperDrift) / atr;
        if (driftAtr > settings.V6RectangleMaxDriftAtr) return null;

        var touchScore = Math.Min(35m,
            (floorTouches + upperTouches) * 35m / (settings.V6PatternMinTouchesPerSide * 4m));
        var containmentScore = Math.Min(35m, containment / 100m * 35m);
        var driftScore = settings.V6RectangleMaxDriftAtr == 0m
            ? 15m
            : Math.Max(0m, 15m * (1m - driftAtr / settings.V6RectangleMaxDriftAtr));
        var widthMiddle = (settings.V6RectangleMinWidthAtr + settings.V6RectangleMaxWidthAtr) / 2m;
        var widthScore = Math.Max(0m, 15m - Math.Abs(widthAtr - widthMiddle) / widthMiddle * 15m);
        var quality = ClampScore(touchScore + containmentScore + driftScore + widthScore);

        return new SidewaysPattern(
            SidewaysPatternKind.Rectangle,
            globalStart,
            globalStart + window.Count - 1,
            floor,
            upper,
            width,
            width,
            floorTouches,
            upperTouches,
            containment,
            quality,
            EventKey(SidewaysPatternKind.Rectangle, window[^1].CloseTime, floor, upper, atr));
    }

    private SidewaysPattern? Triangle(
        IReadOnlyList<Candle> window, int globalStart, EngineSetting settings, decimal atr)
    {
        var pivots = _swings.Detect(window, Math.Max(1, settings.SwingPivotBars));
        var highs = pivots.Where(p => p.IsHigh).ToList();
        var lows = pivots.Where(p => !p.IsHigh).ToList();
        if (highs.Count < settings.V6PatternMinTouchesPerSide
            || lows.Count < settings.V6PatternMinTouchesPerSide)
            return null;

        var highLine = Regression(highs.Select(p => ((decimal)p.Index, p.Price)));
        var lowLine = Regression(lows.Select(p => ((decimal)p.Index, p.Price)));
        var x0 = 0m;
        var x1 = window.Count - 1m;
        var upperStart = highLine.Intercept + highLine.Slope * x0;
        var upperEnd = highLine.Intercept + highLine.Slope * x1;
        var lowerStart = lowLine.Intercept + lowLine.Slope * x0;
        var lowerEnd = lowLine.Intercept + lowLine.Slope * x1;
        var initialWidth = upperStart - lowerStart;
        var endWidth = upperEnd - lowerEnd;

        if (initialWidth <= 0m || endWidth <= atr * 0.35m) return null;
        var contraction = endWidth / initialWidth;
        var converging = highLine.Slope < 0m || lowLine.Slope > 0m;
        if (!converging || contraction > settings.V6TriangleMaxEndWidthFraction) return null;

        var tolerance = atr * 0.20m;
        var contained = 0;
        for (var i = 0; i < window.Count; i++)
        {
            var upper = highLine.Intercept + highLine.Slope * i;
            var lower = lowLine.Intercept + lowLine.Slope * i;
            if (window[i].Close <= upper + tolerance && window[i].Close >= lower - tolerance)
                contained++;
        }

        var containment = (decimal)contained / window.Count * 100m;
        if (containment < settings.V6PatternContainmentPercent) return null;

        var contractionScore = Math.Min(40m, (1m - contraction) * 100m);
        var containmentScore = Math.Min(30m, containment / 100m * 30m);
        var touchScore = Math.Min(30m,
            (highs.Count + lows.Count) * 30m / (settings.V6PatternMinTouchesPerSide * 4m));
        var quality = ClampScore(contractionScore + containmentScore + touchScore);

        return new SidewaysPattern(
            SidewaysPatternKind.Triangle,
            globalStart,
            globalStart + window.Count - 1,
            lowerEnd,
            upperEnd,
            initialWidth,
            endWidth,
            lows.Count,
            highs.Count,
            containment,
            quality,
            EventKey(SidewaysPatternKind.Triangle, window[^1].CloseTime, lowerEnd, upperEnd, atr));
    }

    private static (decimal Slope, decimal Intercept) Regression(
        IEnumerable<(decimal X, decimal Y)> source)
    {
        var points = source.ToList();
        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);
        var denominator = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (denominator == 0m) return (0m, meanY);
        var slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / denominator;
        return (slope, meanY - slope * meanX);
    }

    private static decimal Quantile(IEnumerable<decimal> source, decimal probability)
    {
        var sorted = source.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0m;
        if (sorted.Length == 1) return sorted[0];
        var position = probability * (sorted.Length - 1);
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static int ClampScore(decimal score) => (int)Math.Clamp(
        decimal.Round(score, 0, MidpointRounding.AwayFromZero), 0m, 100m);

    private static string EventKey(
        SidewaysPatternKind kind, DateTime at, decimal floor, decimal upper, decimal atr)
    {
        var step = Math.Max(atr * 0.25m, 0.00000001m);
        var lowBucket = decimal.Round(floor / step, 0, MidpointRounding.AwayFromZero);
        var highBucket = decimal.Round(upper / step, 0, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture,
            $"{kind}:{at:yyyyMMdd}:{lowBucket:0}:{highBucket:0}");
    }
}
