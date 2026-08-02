using MMW.Application.Indicators;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Phân vị theo thứ hạng gần nhất, KHÔNG nội suy (R-009).
/// </summary>
/// <remarks>
/// Có nhiều định nghĩa phân vị cho ra kết quả khác nhau trên cùng dữ liệu. Chốt một định nghĩa
/// và test nó là bắt buộc: nếu kiểm thử lịch sử và chạy thật dùng hai cách tính khác nhau,
/// chúng sẽ lệch nhau đúng ở vùng biên regime — nơi quyết định đổi.
/// </remarks>
public class PercentileTests
{
    private readonly IndicatorService _ind = new();

    /// <summary>1..n để giá trị trùng với thứ hạng, dễ đối chiếu bằng tay.</summary>
    private static List<decimal> OneTo(int n) => Enumerable.Range(1, n).Select(i => (decimal)i).ToList();

    [Fact]
    public void Nearest_rank_theo_dung_cong_thuc_ceil()
    {
        // rank = ceil(p/100 × n); value = sorted[rank-1]
        var v = OneTo(100);

        Assert.Equal(25m, _ind.Percentile(v, 25));   // ceil(0.25×100) = 25 → sorted[24] = 25
        Assert.Equal(75m, _ind.Percentile(v, 75));
        Assert.Equal(90m, _ind.Percentile(v, 90));
        Assert.Equal(100m, _ind.Percentile(v, 100));
    }

    [Fact]
    public void Nearest_rank_lam_tron_LEN_chu_khong_noi_suy()
    {
        // n = 61: ceil(0.25 × 61) = ceil(15.25) = 16 → sorted[15] = 16.
        // Nội suy tuyến tính sẽ cho 15.85 hoặc 16.0 tuỳ biến thể — đó chính là chỗ hai
        // cách tính tách nhau, và là lý do phải chốt một cách.
        var v = OneTo(61);
        Assert.Equal(16m, _ind.Percentile(v, 25));
    }

    [Fact]
    public void Chuoi_khong_sap_xep_van_cho_ket_qua_dung()
    {
        var shuffled = new List<decimal> { 90, 10, 50, 70, 30, 100, 20, 80, 40, 60 };
        var sorted = OneTo(10).Select(x => x * 10m).ToList();

        Assert.Equal(_ind.Percentile(sorted, 90), _ind.Percentile(shuffled, 90));
    }

    [Fact]
    public void Duoi_60_mau_tra_null()
    {
        // Ngưỡng mẫu tối thiểu của R-009. Dưới ngưỡng thì tiêu chí liên quan nhận 0 điểm
        // theo FR-006 — thà không có kết luận còn hơn có một kết luận dựa trên 12 mẫu.
        Assert.Null(_ind.Percentile(OneTo(59), 50));
        Assert.NotNull(_ind.Percentile(OneTo(60), 50));
    }

    [Fact]
    public void Chuoi_rong_tra_null()
    {
        Assert.Null(_ind.Percentile(new List<decimal>(), 50));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void Phan_vi_ngoai_dai_hop_le_thi_nem(int p)
    {
        // Lỗi lập trình, không phải điều kiện dữ liệu.
        Assert.Throws<ArgumentOutOfRangeException>(() => _ind.Percentile(OneTo(100), p));
    }

    // ── PercentileOf: phân vị CỦA một giá trị ───────────────────────────

    [Fact]
    public void PercentileOf_dem_so_phan_tu_nho_hon_hoac_bang()
    {
        // (số phần tử ≤ v) / n × 100
        var v = OneTo(100);

        Assert.Equal(1m, _ind.PercentileOf(v, 1m));
        Assert.Equal(50m, _ind.PercentileOf(v, 50m));
        Assert.Equal(100m, _ind.PercentileOf(v, 100m));
    }

    [Fact]
    public void PercentileOf_gia_tri_ngoai_dai()
    {
        var v = OneTo(100);

        Assert.Equal(0m, _ind.PercentileOf(v, -5m));
        Assert.Equal(100m, _ind.PercentileOf(v, 999m));
    }

    [Fact]
    public void PercentileOf_duoi_nguong_mau_tra_null()
    {
        Assert.Null(_ind.PercentileOf(OneTo(59), 30m));
    }

    [Fact]
    public void Cac_bien_regime_25_75_90_khop_nhau_hai_chieu()
    {
        // Hai hàm phải nhất quán: phân vị của giá trị tại phân vị p phải bằng p.
        // Không nhất quán ở đây nghĩa là VolRegime phân loại sai ngay tại biên.
        var v = OneTo(100);

        foreach (var p in new[] { 25, 75, 90 })
        {
            var value = _ind.Percentile(v, p);
            Assert.NotNull(value);
            Assert.Equal((decimal)p, _ind.PercentileOf(v, value!.Value));
        }
    }
}
