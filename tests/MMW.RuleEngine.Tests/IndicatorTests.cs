using MMW.Application.Indicators;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class IndicatorTests
{
    private readonly IndicatorService _ind = new();

    private static List<decimal> Increasing(int n, decimal start = 100m, decimal step = 1m)
    {
        var list = new List<decimal>(n);
        for (var i = 0; i < n; i++) list.Add(start + step * i);
        return list;
    }

    [Fact]
    public void Sma_Exact()
    {
        var v = new List<decimal> { 1, 2, 3, 4, 5 };
        Assert.Equal(3m, _ind.Sma(v, 5));
        Assert.Equal(4m, _ind.Sma(v, 3)); // (3+4+5)/3
    }

    [Fact]
    public void Sma_Null_When_Insufficient()
    {
        Assert.Null(_ind.Sma(new List<decimal> { 1, 2 }, 5));
    }

    [Fact]
    public void Ema_Lags_Within_Range_For_Increasing()
    {
        var v = Increasing(30);
        var ema = _ind.Ema(v, 10);
        Assert.NotNull(ema);
        Assert.True(ema! < v[^1]);  // EMA trễ hơn giá khi tăng
        Assert.True(ema! > v[0]);
    }

    [Fact]
    public void Rsi_Is_100_When_Only_Gains()
    {
        var v = Increasing(30);
        Assert.Equal(100m, _ind.Rsi(v, 14));
    }

    [Fact]
    public void Rsi_Is_0_When_Only_Losses()
    {
        var v = Increasing(30, start: 200m, step: -1m); // giảm dần
        Assert.Equal(0m, _ind.Rsi(v, 14));
    }

    [Fact]
    public void Rsi_Null_When_Insufficient()
    {
        Assert.Null(_ind.Rsi(Increasing(10), 14));
    }

    [Fact]
    public void Macd_Positive_For_Uptrend()
    {
        var v = Increasing(60);
        var macd = _ind.Macd(v);
        Assert.NotNull(macd.Macd);
        Assert.True(macd.Macd! > 0);          // fast EMA > slow EMA khi tăng
        Assert.NotNull(macd.Signal);
        Assert.NotNull(macd.Histogram);
    }

    [Fact]
    public void Atr_Is_Positive()
    {
        var candles = new List<Candle>();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal price = 100m;
        for (var i = 0; i < 30; i++)
        {
            var open = price;
            var close = price + (i % 2 == 0 ? 2m : -1m);
            var high = Math.Max(open, close) + 1m;
            var low = Math.Min(open, close) - 1m;
            candles.Add(new Candle(baseTime.AddHours(i), open, high, low, close, 10m, baseTime.AddHours(i + 1)));
            price = close;
        }

        var atr = _ind.Atr(candles, 14);
        Assert.NotNull(atr);
        Assert.True(atr! > 0);
    }

    [Fact]
    public void Atr_Null_When_Insufficient()
    {
        var candles = new List<Candle>
        {
            new(DateTime.UtcNow, 100, 101, 99, 100, 1, DateTime.UtcNow),
        };
        Assert.Null(_ind.Atr(candles, 14));
    }

    // ─────────────────────────────────────────────────────────────────────
    // SC-004 — chống repaint
    //
    // Chỉ báo phải là hàm CHỈ của các nến đã đóng. Hệ quả kiểm chứng được:
    // trong lúc một cây nến còn đang chạy, giá của nó nhảy thế nào cũng
    // KHÔNG được làm đổi bất kỳ giá trị chỉ báo nào.
    //
    // Đây là điều kiện tiên quyết của toàn bộ feature: chỉ báo còn repaint thì
    // kiểm thử lịch sử vĩnh viễn không tái lập được kết quả chạy thật, và mọi
    // con số backtest phía sau đều là số nói dối.
    // ─────────────────────────────────────────────────────────────────────

    private static readonly DateTime Anchor = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>n nến 15 phút đã đóng, bắt đầu từ <see cref="Anchor"/>.</summary>
    private static List<Candle> ClosedSeries(int n)
    {
        var list = new List<Candle>(n);
        for (var i = 0; i < n; i++)
        {
            var open = Anchor.AddMinutes(15 * i);
            var close = 100m + i;
            list.Add(new Candle(open, close - 0.5m, close + 1m, close - 1m, close, 10m,
                open.AddMinutes(15).AddMilliseconds(-1)));
        }
        return list;
    }

    /// <summary>Gắn thêm một cây nến ĐANG CHẠY vào cuối chuỗi, với giá đóng tuỳ ý.</summary>
    private static List<Candle> WithRunningCandle(List<Candle> closed, decimal runningClose)
    {
        var open = closed[^1].CloseTime.AddMilliseconds(1);
        var withRunning = new List<Candle>(closed)
        {
            new(open, closed[^1].Close, Math.Max(closed[^1].Close, runningClose) + 1m,
                Math.Min(closed[^1].Close, runningClose) - 1m, runningClose, 3m,
                open.AddMinutes(15).AddMilliseconds(-1)),
        };
        return withRunning;
    }

    [Fact]
    public void Chi_bao_khong_doi_du_nen_dang_chay_nhay_gia_manh()
    {
        var closed = ClosedSeries(60);
        var midCandle = closed[^1].CloseTime.AddMinutes(7);   // giữa chu kỳ nến kế tiếp
        var analyzer = new MarketAnalyzer(_ind, TestClock.At(midCandle));

        var spike = analyzer.Analyze(WithRunningCandle(closed, 5_000m), currentPrice: 5_000m);
        var crash = analyzer.Analyze(WithRunningCandle(closed, 1m), currentPrice: 1m);

        Assert.Equal(spike.Ema20, crash.Ema20);
        Assert.Equal(spike.Ema50, crash.Ema50);
        Assert.Equal(spike.Rsi, crash.Rsi);
        Assert.Equal(spike.Macd, crash.Macd);
        Assert.Equal(spike.MacdSignal, crash.MacdSignal);
        Assert.Equal(spike.MacdHistogram, crash.MacdHistogram);
        Assert.Equal(spike.Atr, crash.Atr);
    }

    [Fact]
    public void Bias_van_phan_ung_theo_gia_hien_tai_va_do_la_dung()
    {
        // Ranh giới dễ hiểu nhầm: Bias và Score KHÔNG nằm trong tập bị đóng băng.
        // Chúng so GIÁ HIỆN TẠI với EMA đã đóng băng, nên đổi theo giá là đúng chức năng
        // chứ không phải repaint. Repaint là khi giá trị của một cây nến trong QUÁ KHỨ
        // đổi về sau; ở đây chỉ có "hiện tại" đang đổi.
        //
        // Test này tồn tại để lần sau ai đó thấy Bias thiếu trong danh sách assertion phía
        // trên thì biết đó là chủ ý, không phải assertion bị bỏ quên.
        var closed = ClosedSeries(60);
        var midCandle = closed[^1].CloseTime.AddMinutes(7);
        var analyzer = new MarketAnalyzer(_ind, TestClock.At(midCandle));

        var spike = analyzer.Analyze(WithRunningCandle(closed, 5_000m), currentPrice: 5_000m);
        var crash = analyzer.Analyze(WithRunningCandle(closed, 1m), currentPrice: 1m);

        Assert.NotEqual(spike.Bias, crash.Bias);
    }

    [Fact]
    public void Gia_hien_tai_van_bam_sat_thi_truong_du_chi_bao_dong_bang()
    {
        // Mặt còn lại của cùng một đồng xu: đóng băng chỉ báo KHÔNG được làm
        // đóng băng giá. Giá hiện tại đến từ tham số riêng, không từ chuỗi nến.
        var closed = ClosedSeries(60);
        var midCandle = closed[^1].CloseTime.AddMinutes(7);
        var analyzer = new MarketAnalyzer(_ind, TestClock.At(midCandle));

        var spike = analyzer.Analyze(WithRunningCandle(closed, 5_000m), currentPrice: 5_000m);
        var crash = analyzer.Analyze(WithRunningCandle(closed, 1m), currentPrice: 1m);

        Assert.Equal(5_000m, spike.Price);
        Assert.Equal(1m, crash.Price);
    }

    [Fact]
    public void Tinh_giua_chu_ky_bang_dung_tinh_lai_khi_da_bo_nen_ho()
    {
        // Cách phát biểu trực tiếp của SC-004: kết quả lúc nến 61 đang chạy phải
        // TRÙNG KHỚP với kết quả tính trên đúng 60 nến đã đóng.
        var closed = ClosedSeries(60);
        var midCandle = closed[^1].CloseTime.AddMinutes(7);
        var analyzer = new MarketAnalyzer(_ind, TestClock.At(midCandle));

        var duringCandle = analyzer.Analyze(WithRunningCandle(closed, 777m), currentPrice: 777m);
        var closedOnly = analyzer.Analyze(closed, currentPrice: 777m);

        Assert.Equal(closedOnly, duringCandle);
    }
}
