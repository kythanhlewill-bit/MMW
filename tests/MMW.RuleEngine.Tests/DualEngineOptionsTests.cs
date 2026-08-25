using MMW.Application.Trading;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Công tắc chạy hai bộ luật song song. Toàn bộ cách tách dựa trên một tiền đề duy nhất: hai
/// danh sách mã KHÔNG giao nhau. Mất tiền đề đó thì hai bộ luật lại gặp nhau trên cùng một mã
/// và mọi thứ dựng ở trên nó — ký quỹ riêng, khoá chống trùng, chiều lệnh — sập theo.
/// </summary>
public class DualEngineOptionsTests
{
    private static readonly string[] Intraday = ["BTCUSDT", "ETHUSDT"];

    [Fact]
    public void Tat_thi_khong_bao_gio_bao_loi()
    {
        var o = new DualEngineOptions { Enabled = false, HtfSymbols = "BTCUSDT" };
        Assert.Null(o.Validate(Intraday));
    }

    [Fact]
    public void Cau_hinh_hop_le_thi_qua()
    {
        var o = new DualEngineOptions { Enabled = true, HtfSymbols = "BTCUSDC,ETHUSDC" };
        Assert.Null(o.Validate(Intraday));
    }

    /// <summary>Đây là lỗi cấu hình mà cả thiết kế này sinh ra để tránh.</summary>
    [Theory]
    [InlineData("BTCUSDC,BTCUSDT")]
    [InlineData("btcusdt")]
    public void Trung_ma_voi_duong_trong_ngay_thi_bi_tu_choi(string htf)
    {
        var o = new DualEngineOptions { Enabled = true, HtfSymbols = htf };

        var problem = o.Validate(Intraday);

        Assert.NotNull(problem);
        Assert.Contains("trùng mã", problem);
    }

    /// <summary>Bật mà không khai mã nào là bật hụt, không phải bật.</summary>
    [Fact]
    public void Bat_ma_khong_co_ma_nao_thi_bi_tu_choi()
    {
        var o = new DualEngineOptions { Enabled = true, HtfSymbols = "  ,  " };

        var problem = o.Validate(Intraday);

        Assert.NotNull(problem);
        Assert.Contains("HtfSymbols trống", problem);
    }

    [Fact]
    public void Danh_sach_ma_duoc_chuan_hoa_va_bo_trung()
    {
        var o = new DualEngineOptions { HtfSymbols = " btcusdc , ETHUSDC ,BTCUSDC" };

        Assert.Equal(new[] { "BTCUSDC", "ETHUSDC" }, o.HtfSymbolList());
    }
}
