using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Nến chưa đóng là gốc của lỗi repaint: chỉ báo tính trên nến đang chạy sẽ đổi giá trị
/// theo từng tick, nên kiểm thử lịch sử không bao giờ tái lập được kết quả chạy thật.
/// <c>ClosedOnly()</c> là chốt chặn duy nhất — nếu nó sai thì mọi con số phía sau đều sai.
/// </summary>
public class CandleExtensionsTests
{
    private static readonly DateTime T0 = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Chuỗi nến 15 phút, đóng tại mốc trừ 1 ms — đúng cách Binance đánh dấu closeTime.</summary>
    private static List<Candle> Series(int count, DateTime startOpen)
    {
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++)
        {
            var open = startOpen.AddMinutes(15 * i);
            list.Add(new Candle(open, 100m, 101m, 99m, 100.5m, 10m, open.AddMinutes(15).AddMilliseconds(-1)));
        }
        return list;
    }

    [Fact]
    public void Chuoi_rong_tra_ve_chuoi_rong()
    {
        var result = new List<Candle>().ClosedOnly(TestClock.At(T0));
        Assert.Empty(result);
    }

    [Fact]
    public void Moi_nen_da_dong_thi_giu_nguyen_toan_bo()
    {
        var candles = Series(10, T0.AddHours(-3));

        var result = candles.ClosedOnly(TestClock.At(T0));

        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Cat_dung_mot_nen_dang_chay_o_cuoi_chuoi()
    {
        // 10 nến bắt đầu 09:30; nến cuối mở 11:45 và đóng 11:59:59.999.
        // Đặt đồng hồ 11:50 → nến cuối CHƯA đóng.
        var candles = Series(10, new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Utc));
        var clock = TestClock.At(new DateTime(2026, 3, 2, 11, 50, 0, DateTimeKind.Utc));

        var result = candles.ClosedOnly(clock);

        Assert.Equal(9, result.Count);
        Assert.Equal(candles[8].OpenTime, result[^1].OpenTime);
    }

    [Fact]
    public void Toan_bo_nen_deu_ho_thi_tra_ve_rong()
    {
        var candles = Series(5, T0.AddHours(1));

        var result = candles.ClosedOnly(TestClock.At(T0));

        Assert.Empty(result);
    }

    [Fact]
    public void Nen_dong_dung_mot_khoanh_khac_hien_tai_duoc_GIU_LAI()
    {
        // Ranh giới quan trọng nhất: CloseTime == UtcNow nghĩa là ĐÃ đóng.
        // Sai chiều so sánh ở đây làm lệch toàn bộ chuỗi đúng một nến, và lệch
        // đó đủ để test tương đương backtest↔live đỏ mà không rõ lý do.
        var open = T0.AddMinutes(-15);
        var candle = new Candle(open, 100m, 101m, 99m, 100.5m, 10m, T0);

        var result = new List<Candle> { candle }.ClosedOnly(TestClock.At(T0));

        Assert.Single(result);
    }

    [Fact]
    public void Nen_dong_sau_hien_tai_dung_mot_tick_thi_bi_cat()
    {
        var open = T0.AddMinutes(-15);
        var candle = new Candle(open, 100m, 101m, 99m, 100.5m, 10m, T0.AddTicks(1));

        var result = new List<Candle> { candle }.ClosedOnly(TestClock.At(T0));

        Assert.Empty(result);
    }

    [Fact]
    public void Nen_chua_dong_nam_giua_chuoi_thi_NEM_chu_khong_loc_am_tham()
    {
        // Chuỗi không tăng dần theo thời gian là lỗi lập trình. Lọc bỏ phần tử ở giữa
        // sẽ tạo chuỗi có lỗ hổng, và chỉ báo tính trên chuỗi thủng thì sai trong im lặng —
        // đúng kiểu hỏng tệ nhất. Phải nổ ngay.
        var candles = Series(5, T0.AddHours(-2));
        candles[2] = candles[2] with { CloseTime = T0.AddHours(5) };

        Assert.Throws<ArgumentException>(() => candles.ClosedOnly(TestClock.At(T0)));
    }

    [Fact]
    public void IsClosed_theo_dung_mo_ta_hop_dong()
    {
        var candle = new Candle(T0.AddMinutes(-15), 100m, 101m, 99m, 100.5m, 10m, T0);

        Assert.True(candle.IsClosed(TestClock.At(T0)));
        Assert.True(candle.IsClosed(TestClock.At(T0.AddTicks(1))));
        Assert.False(candle.IsClosed(TestClock.At(T0.AddTicks(-1))));
    }
}
