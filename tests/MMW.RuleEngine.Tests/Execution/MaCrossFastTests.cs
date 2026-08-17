using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Vào ngay khi MA7 cắt MA25 trên khung 5m kèm khối lượng mạnh.
/// </summary>
/// <remarks>
/// Nhánh sớm nhất và rủi ro nhất trong năm pha: chưa có nhịp hồi nào xác nhận xu hướng, chỉ có
/// cú đẩy. Lợi thế duy nhất của nó là thấy cú cắt TRƯỚC khi nến 15m đóng — nên mọi ràng buộc ở
/// đây tồn tại để bảo vệ đúng lợi thế đó: cửa sổ 3 nến 5m (= một nến 15m) và ngưỡng khối lượng
/// cao hơn ba nhánh còn lại.
/// </remarks>
public sealed class MaCrossFastTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure, new SidewaysPatternAnalyzer(ScoringFixtures.Swings));

    [Fact]
    public void Cu_cat_vua_xay_ra_kem_khoi_luong_manh_thi_xac_nhan()
    {
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: FastCross());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.MaCrossFast, result.SetupType);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);
    }

    /// <summary>Nhánh này KHÔNG đặt lệnh chờ — giá trị của nó nằm ở chỗ vào sớm.</summary>
    /// <remarks>
    /// Bốn nhánh còn lại chờ giá quay về một mức đã biết nên đặt lệnh chờ được. Nhánh này mua
    /// đúng thứ mà lệnh chờ có thể làm mất: sự có mặt ngay lúc xu hướng vừa đổi.
    /// </remarks>
    [Fact]
    public void Khong_dat_lenh_cho()
    {
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: FastCross());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        Assert.Null(result.SuggestedLimitEntry);
    }

    [Fact]
    public void Chot_loi_dat_2R_va_dung_lo_ton_trong_san()
    {
        var settings = ScoringFixtures.Settings(s => s.MinStopDistancePercent = 0.40m);
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: FastCross(), settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        var entry = context.CurrentPrice;
        var risk = entry - result.SuggestedStopLoss!.Value;
        Assert.True(risk > 0m);
        Assert.True(risk / entry * 100m >= 0.40m);
        Assert.Equal(2m, Math.Round((result.SuggestedFirstTakeProfit!.Value - entry) / risk, 4));
    }

    [Fact]
    public void Khoi_luong_khong_du_thi_khong_vao()
    {
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: FastCross(volume: 1.2m));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Contains("Cắt MA nhanh:", result.DetailVi);
    }

    /// <summary>Cú cắt cũ hơn 3 nến 5m thì hết lợi thế — nhường lại cho nhánh hồi về MA.</summary>
    [Fact]
    public void Cu_cat_da_cu_thi_khong_vao()
    {
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: FastCross(barsAfterCross: 10));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.NotEqual(SetupType.MaCrossFast, result.SetupType);
    }

    /// <summary>Không có nến khung nhanh thì nhánh này tự đứng ngoài, không kéo sập vòng chấm.</summary>
    [Fact]
    public void Thieu_nen_khung_nhanh_thi_bo_qua_lang_le()
    {
        var context = ScoringFixtures.Context(entry: EntryFlat(), fast: Array.Empty<Candle>());

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.DoesNotContain("Cắt MA nhanh:", result.DetailVi);
    }

    /// <summary>Nến khung vào lệnh: phẳng tại 100, nên CurrentPrice = 100.</summary>
    private static List<Candle> EntryFlat() =>
        ScoringFixtures.Flat(80, price: 100m, range: 2m, volume: 100m).ToList();

    /// <summary>Nền phẳng → cú đẩy tăng làm MA7 cắt lên MA25, kết thúc cách đây `barsAfterCross` nến.</summary>
    private static List<Candle> FastCross(decimal volume = 2.5m, int barsAfterCross = 1)
    {
        var candles = new List<Candle>();
        var i = 0;

        Candle Bar(decimal close, decimal high, decimal low, decimal vol)
        {
            var open = Start.AddMinutes(5 * i++);
            return new Candle(open, close, high, low, close, vol, open.AddMinutes(5).AddTicks(-1));
        }

        // Kết thúc ĐÚNG tại 100 để khớp CurrentPrice do nến khung vào lệnh quy định — dừng lỗ
        // đọc từ khung nhanh còn giá vào đọc từ khung vào lệnh, hai thang lệch nhau là vô nghĩa.
        for (var n = 0; n < 50; n++) candles.Add(Bar(95.5m, 95.8m, 95.2m, 100m));
        for (var n = 3; n >= 1; n--)
        {
            var p = 100m - (n - 1) * 1.5m;
            candles.Add(Bar(p, p + 0.3m, p - 0.3m, 100m * volume));
        }

        // Nến trôi sau cú cắt: dùng để đẩy cú cắt ra ngoài cửa sổ 3 nến khi cần.
        var top = candles[^1].Close;
        for (var n = 0; n < barsAfterCross - 1; n++)
            candles.Add(Bar(top, top + 0.3m, top - 0.3m, 100m));

        return candles;
    }
}
