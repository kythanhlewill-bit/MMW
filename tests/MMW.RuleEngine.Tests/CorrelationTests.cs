using MMW.Application.Indicators;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Tương quan với mã dẫn dắt — tiêu chí <c>market.leader_correlation</c> dựa vào phép tính này.
/// </summary>
public class CorrelationTests
{
    private static readonly IIndicatorService Indicators = new IndicatorService();

    private static List<decimal> Series(int count, Func<int, decimal> f) =>
        Enumerable.Range(0, count).Select(f).ToList();

    [Fact]
    public void Hai_chuoi_giong_het_nhau_cho_tuong_quan_bang_1()
    {
        var a = Series(60, i => i % 7 - 3m);

        Assert.Equal(1m, Indicators.Correlation(a, a)!.Value, 6);
    }

    [Fact]
    public void Chuoi_nghich_dao_cho_tuong_quan_bang_am_1()
    {
        var a = Series(60, i => i % 7 - 3m);
        var b = a.Select(v => -v).ToList();

        Assert.Equal(-1m, Indicators.Correlation(a, b)!.Value, 6);
    }

    [Fact]
    public void Duoi_nguong_mau_thi_tra_null_chu_khong_tra_mot_con_so_yeu()
    {
        var a = Series(IndicatorService.MinCorrelationSamples - 1, i => i);

        Assert.Null(Indicators.Correlation(a, a));
    }

    [Fact]
    public void Hai_chuoi_lech_do_dai_thi_tra_null()
    {
        Assert.Null(Indicators.Correlation(Series(60, i => i), Series(59, i => i)));
    }

    /// <summary>
    /// Chuỗi phẳng tuyệt đối trả null, KHÔNG trả 0.
    /// </summary>
    /// <remarks>
    /// Phương sai bằng 0 làm hệ số không xác định. Trả 0 ở đó sẽ được tiêu chí đọc thành "đã đo
    /// được và bằng 0" ⟹ chấm 0/4 điểm vì "chuyển động rời rạc", trong khi sự thật là mã đó
    /// không giao dịch. Hai kết luận khác hẳn nhau.
    /// </remarks>
    [Fact]
    public void Chuoi_phang_tuyet_doi_tra_null_chu_khong_tra_khong()
    {
        var flat = Series(60, _ => 100m);
        var moving = Series(60, i => i);

        Assert.Null(Indicators.Correlation(flat, moving));
    }

    /// <summary>
    /// Tương quan phải tính trên LỢI SUẤT, không phải trên giá.
    /// </summary>
    /// <remarks>
    /// Đây là lý do <see cref="IIndicatorService.LogReturns"/> tồn tại. Hai chuỗi giá cùng có xu
    /// hướng tăng luôn cho tương quan gần 1 dù chuyển động ngày qua ngày chẳng liên quan gì
    /// nhau. Test dựng đúng cái bẫy đó: hai đường giá cùng đi lên, nhưng nhịp dao động quanh xu
    /// hướng thì ngược pha hoàn toàn.
    /// </remarks>
    [Fact]
    public void Tuong_quan_tren_gia_bi_xu_huong_chung_danh_lua_con_tren_loi_suat_thi_khong()
    {
        var a = Series(60, i => 1000m + i * 10m + (i % 2 == 0 ? 5m : -5m));
        var b = Series(60, i => 2000m + i * 10m + (i % 2 == 0 ? -5m : 5m));

        var onPrice = Indicators.Correlation(a, b)!.Value;
        var onReturns = Indicators.Correlation(Indicators.LogReturns(a), Indicators.LogReturns(b))!.Value;

        Assert.True(onPrice > 0.9m, $"Tương quan trên giá phải bị xu hướng chung kéo lên gần 1, thực tế {onPrice}.");
        Assert.True(onReturns < 0m, $"Tương quan trên lợi suất phải âm vì hai nhịp ngược pha, thực tế {onReturns}.");
    }

    [Fact]
    public void Log_return_ngan_hon_chuoi_gia_dung_mot_phan_tu()
    {
        Assert.Equal(59, Indicators.LogReturns(Series(60, i => 1000m + i)).Count);
    }

    [Fact]
    public void Chuoi_gia_qua_ngan_khong_sinh_loi_suat_nao()
    {
        Assert.Empty(Indicators.LogReturns(new[] { 1000m }));
        Assert.Empty(Indicators.LogReturns(Array.Empty<decimal>()));
    }
}
