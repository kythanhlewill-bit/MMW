using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Nhịp hồi sâu về MA99 sau khi giá bị từ chối rõ ở kháng cự.
/// </summary>
/// <remarks>
/// Nhịp cuối của một xu hướng đang chín. Hai điều khác hẳn ba nhánh MA còn lại, và cả hai đều
/// đến từ chính cú từ chối:
///
/// • Mục tiêu đặt ĐÚNG tại mức bị từ chối, không theo bội R. Cú từ chối nói thị trường sắp đi
///   ngang, và trong đi ngang thì mức đó là biên trên — đòi vượt nó là đòi một cú phá biên mà
///   chính cú từ chối vừa nói là chưa tới.
/// • Tỉ lệ tối thiểu hạ xuống 1,0R. Dừng lỗ nằm dưới MA99 nên nó rộng; đòi 1,6R như các nhánh
///   khác sẽ loại sạch nhóm setup mà cả nhánh này nhắm tới.
/// </remarks>
public sealed class MaDeepPullbackTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure, new SidewaysPatternAnalyzer(ScoringFixtures.Swings));

    [Fact]
    public void Tu_choi_ro_roi_hoi_sau_ve_MA99_thi_xac_nhan()
    {
        var context = ScoringFixtures.Context(entry: DeepPullback());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.MaDeepPullback, result.SetupType);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);
    }

    /// <summary>Mục tiêu là chính mức bị từ chối, không phải một bội R nào.</summary>
    [Fact]
    public void Muc_tieu_dat_tai_muc_bi_tu_choi()
    {
        var context = ScoringFixtures.Context(entry: DeepPullback());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        var rejectionHigh = context.EntryCandles.TakeLast(30).Max(c => c.High);

        // Lùi vào một khoảng đệm nên phải THẤP hơn đỉnh, nhưng không thấp hơn nhiều.
        Assert.True(result.SuggestedFirstTakeProfit! < rejectionHigh);
        Assert.True(result.SuggestedFirstTakeProfit! > rejectionHigh * 0.97m,
            $"Mục tiêu {result.SuggestedFirstTakeProfit} rời quá xa đỉnh {rejectionHigh}.");
    }

    [Fact]
    public void Dung_lo_nam_duoi_MA99_va_ton_trong_san()
    {
        var settings = ScoringFixtures.Settings(s => s.MinStopDistancePercent = 0.40m);
        var context = ScoringFixtures.Context(entry: DeepPullback(), settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        var ma99 = context.EntryCandles.TakeLast(99).Average(c => c.Close);
        Assert.True(result.SuggestedStopLoss! < ma99, "Dừng lỗ phải nằm DƯỚI MA99.");

        var percent = (context.CurrentPrice - result.SuggestedStopLoss!.Value) / context.CurrentPrice * 100m;
        Assert.True(percent >= 0.40m, $"Dừng lỗ chỉ cách {percent:N3}%.");
    }

    /// <summary>Không có râu nến đủ dài thì không có cú từ chối, và nhánh này đứng ngoài.</summary>
    [Fact]
    public void Khong_co_cu_tu_choi_thi_khong_vao()
    {
        var context = ScoringFixtures.Context(entry: DeepPullback(rejectionWick: 0m));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.NotEqual(SetupType.MaDeepPullback, result.SetupType);
    }

    /// <summary>Có cú từ chối nhưng giá còn ở xa MA99 thì chưa phải nhịp hồi sâu.</summary>
    [Fact]
    public void Gia_chua_ve_vung_MA99_thi_khong_vao()
    {
        var context = ScoringFixtures.Context(entry: DeepPullback(finalPrice: 118m));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Contains("Hồi sâu:", result.DetailVi);
        Assert.Contains("chưa về vùng MA99", result.DetailVi);
    }

    /// <summary>
    /// Xu hướng lớn dựng dần lên, một cú từ chối ở đỉnh, rồi giá hồi sâu về vùng MA99.
    /// </summary>
    /// <remarks>
    /// Cố ý KHÔNG đòi MA7 thuận chiều: ở nhịp hồi sâu thì MA nhanh gần như luôn đã cắt xuống, và
    /// đó chính là hình dạng của setup này chứ không phải dấu hiệu hỏng.
    /// </remarks>
    private static List<Candle> DeepPullback(decimal rejectionWick = 4m, decimal finalPrice = 0m)
    {
        var candles = new List<Candle>();
        var i = 0;

        Candle Bar(decimal close, decimal high, decimal low, decimal vol = 100m)
        {
            var open = Start.AddMinutes(15 * i++);
            return new Candle(open, close, high, low, close, vol, open.AddMinutes(15).AddTicks(-1));
        }

        // 120 nến đi lên chậm từ 100 → 112, đủ để MA25 nằm trên MA99.
        for (var n = 0; n < 120; n++)
        {
            var p = 100m + n * 0.1m;
            candles.Add(Bar(p, p + 0.4m, p - 0.4m));
        }

        // Cú từ chối: giá chọc lên rồi bị đẩy về, để lại râu trên dài.
        var peak = candles[^1].Close + 2m;
        candles.Add(Bar(peak, peak + rejectionWick, peak - 0.4m));

        // Hồi sâu 20 nến về lại vùng MA99. MA99 lúc này quanh 106–107.
        var target = finalPrice > 0m ? finalPrice : 108.6m;
        var from = candles[^1].Close;
        for (var n = 1; n <= 20; n++)
        {
            var p = from + (target - from) * n / 20m;
            candles.Add(Bar(p, p + 0.4m, p - 0.4m));
        }

        return candles;
    }
}
