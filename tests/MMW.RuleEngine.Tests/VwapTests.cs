using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// VWAP neo theo ngày UTC, khởi động lại đúng 00:00 (R-008).
/// </summary>
/// <remarks>
/// Một mốc neo duy nhất dùng chung khắp hệ thống — trùng mốc ngày giao dịch của FR-024,
/// trùng nến ngày đóng, trùng một mốc thanh toán phí vốn. Đây là cách loại bỏ cả một lớp
/// lỗi lệch múi giờ, và test dưới đây là thứ giữ cho nó không trôi.
/// </remarks>
public class VwapTests
{
    private readonly IndicatorService _ind = new();

    /// <summary>Nến 15 phút với giá điển hình cố định và khối lượng cho trước.</summary>
    private static Candle Bar(DateTime openUtc, decimal typicalPrice, decimal volume) =>
        new(openUtc, typicalPrice, typicalPrice, typicalPrice, typicalPrice, volume,
            openUtc.AddMinutes(15).AddMilliseconds(-1));

    [Fact]
    public void Vwap_la_trung_binh_gia_theo_khoi_luong_trong_ngay()
    {
        var d = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            Bar(d,                 100m, 10m),
            Bar(d.AddMinutes(15),  200m, 30m),
        };

        // (100×10 + 200×30) / 40 = 7000/40 = 175
        Assert.Equal(175m, _ind.AnchoredVwap(candles));
    }

    [Fact]
    public void Vwap_khoi_dong_lai_dung_00_00_UTC()
    {
        // Nến của ngày hôm trước KHÔNG được ảnh hưởng tới VWAP hôm nay, dù giá lệch rất xa.
        var homQua = new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc);
        var homNay = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

        var candles = new List<Candle>
        {
            Bar(homQua,               9_999m, 1_000m),   // giá cực lệch, khối lượng lớn
            Bar(homQua.AddMinutes(15), 9_999m, 1_000m),
            Bar(homNay,                100m,     10m),
            Bar(homNay.AddMinutes(15), 200m,     30m),
        };

        Assert.Equal(175m, _ind.AnchoredVwap(candles));
    }

    [Fact]
    public void Vwap_dung_gia_dien_hinh_HLC_chia_3()
    {
        var d = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            new(d, 100m, 120m, 90m, 105m, 10m, d.AddMinutes(15).AddMilliseconds(-1)),
        };

        // (120 + 90 + 105) / 3 = 105
        Assert.Equal(105m, _ind.AnchoredVwap(candles));
    }

    [Fact]
    public void Chuoi_rong_tra_null()
    {
        Assert.Null(_ind.AnchoredVwap(new List<Candle>()));
    }

    [Fact]
    public void Khoi_luong_ngay_bang_khong_tra_null()
    {
        var d = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        Assert.Null(_ind.AnchoredVwap(new List<Candle> { Bar(d, 100m, 0m) }));
    }

    [Fact]
    public void Chi_lay_nen_cua_ngay_UTC_cua_nen_CUOI_chuoi()
    {
        // Neo bám theo nến mới nhất trong chuỗi, không theo đồng hồ — nhờ vậy hàm vẫn
        // thuần và kiểm thử lịch sử dùng được y hệt chạy thật.
        var d = new DateTime(2026, 3, 2, 23, 45, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            Bar(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), 100m, 10m),
            Bar(d, 300m, 10m),
        };

        // Cả hai cùng ngày 2026-03-02 → (100×10 + 300×10)/20 = 200
        Assert.Equal(200m, _ind.AnchoredVwap(candles));
    }

    // ── VolumeSma ───────────────────────────────────────────────────────

    [Fact]
    public void VolumeSma_trung_binh_khoi_luong_n_nen_gan_nhat()
    {
        var d = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle>();
        for (var i = 0; i < 25; i++) candles.Add(Bar(d.AddMinutes(15 * i), 100m, 10m));
        candles.Add(Bar(d.AddMinutes(15 * 25), 100m, 100m));   // nến đột biến ở cuối

        // 19 nến khối lượng 10 + 1 nến khối lượng 100, trên cửa sổ 20 → (190 + 100)/20 = 14.5
        Assert.Equal(14.5m, _ind.VolumeSma(candles, 20));
    }

    [Fact]
    public void VolumeSma_thieu_du_lieu_tra_null()
    {
        var d = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle> { Bar(d, 100m, 10m) };

        Assert.Null(_ind.VolumeSma(candles, 20));
    }
}
