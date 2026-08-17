using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Nhịp hồi về MA nhanh: xu hướng đọc từ MA7/MA25, vào khi giá hồi về chạm MA7.
/// </summary>
/// <remarks>
/// Bộ dò này được thêm sau khi đo 8 ngày chạy thật: <c>TrendPullback</c> (đọc xu hướng từ chuỗi
/// phá cấu trúc) kích hoạt <b>0 lần</b>, trong khi giá chạm MA7 thuận xu hướng xảy ra 142–160
/// lần mỗi mã. Giá trị của nó không nằm ở chỗ đoán đúng hơn mà ở chỗ ĐẶT DỪNG LỖ RỘNG HƠN: vào
/// tại MA và dừng dưới vùng tích luỹ cho bề rộng 0,3–1%, còn bộ dò cũ vào ngay tại điểm phá và
/// dừng sát mức đó, ra 0,14–0,25% — dải mà phí ăn 1,5–9,6R.
/// </remarks>
public sealed class MaPullbackTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure, new SidewaysPatternAnalyzer(ScoringFixtures.Swings));

    // ── Nhận đúng nhịp hồi ──────────────────────────────────────────────

    [Fact]
    public void Nhip_hoi_ve_MA7_thuan_xu_huong_thi_xac_nhan()
    {
        var context = ScoringFixtures.Context(entry: UptrendPullback());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.MaPullback, result.SetupType);
        Assert.Equal(SetupTriggerState.Confirmed, result.State);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);
        Assert.True(result.SetupQualityScore >= 60, $"Chất lượng {result.SetupQualityScore} phải ≥ 60.");
    }

    /// <summary>
    /// Nhịp hồi ĐẦU đặt 2R, nhịp thứ HAI hạ xuống 1,5R.
    /// </summary>
    /// <remarks>
    /// Luật suy giảm này đến từ chính người dùng: nhịp sau đi kèm khối lượng đã cạn, nên đòi 2R
    /// ở đó là đổi một mục tiêu thường chạm được lấy một mục tiêu thường hụt.
    ///
    /// Thứ tự nhịp đọc thẳng từ lịch sử nến (đếm sự kiện chạm MA kể từ điểm cắt), nên không cần
    /// lưu trạng thái nào giữa các chu kỳ chấm điểm.
    /// </remarks>
    [Theory]
    [InlineData(0.5, 2.0)]   // đỉnh dốc lên: MA7 không đuổi kịp ⟹ đây là lần chạm ĐẦU
    [InlineData(0.0, 1.5)]   // đỉnh đi ngang: MA7 đuổi kịp và đã chạm ⟹ đây là lần THỨ HAI
    public void Chot_loi_giam_dan_theo_thu_tu_nhip_hoi(double topStep, double expectedR)
    {
        var context = ScoringFixtures.Context(entry: UptrendPullback(topStep: (decimal)topStep));

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed, result.DetailVi);
        var entry = context.CurrentPrice;
        var risk = entry - result.SuggestedStopLoss!.Value;
        Assert.True(risk > 0m);
        Assert.Equal(
            (decimal)expectedR,
            Math.Round((result.SuggestedFirstTakeProfit!.Value - entry) / risk, 4));
    }

    /// <summary>
    /// Dừng lỗ phải tôn trọng sàn phần trăm — quyết định này GHI ĐÈ mức của planner.
    /// </summary>
    /// <remarks>
    /// <c>SignalEvalService</c> lấy <c>trigger.SuggestedStopLoss ?? context.PlannedStopLoss</c>,
    /// nên sàn dựng trong <c>StructuralLevelPlanner</c> không với tới quyết định này. Bỏ sót chỗ
    /// đó là tái lập đúng bệnh dừng lỗ 1–7 bps mà Bước 0 vừa vá.
    /// </remarks>
    [Fact]
    public void Dung_lo_ton_trong_san_phan_tram()
    {
        var settings = ScoringFixtures.Settings(s => s.MinStopDistancePercent = 0.40m);
        var context = ScoringFixtures.Context(entry: UptrendPullback(), settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        var percent = (context.CurrentPrice - result.SuggestedStopLoss!.Value) / context.CurrentPrice * 100m;
        Assert.True(percent >= 0.40m, $"Dừng lỗ chỉ cách {percent:N3}%, dưới sàn 0,40%.");
    }

    /// <summary>Vào bằng lệnh chờ đặt ngay tại MA nhanh — đúng mức mà phương pháp này chờ.</summary>
    [Fact]
    public void Muc_cho_dat_tai_MA_nhanh()
    {
        var context = ScoringFixtures.Context(entry: UptrendPullback());

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed);
        Assert.NotNull(result.SuggestedLimitEntry);
        var candles = context.EntryCandles;
        var ma7 = candles.TakeLast(7).Average(c => c.Close);
        Assert.Equal(Math.Round(ma7, 6), Math.Round(result.SuggestedLimitEntry!.Value, 6));
    }

    // ── Từ chối đúng chỗ ────────────────────────────────────────────────

    [Fact]
    public void Chieu_nguoc_chong_MA_thi_tu_choi()
    {
        var context = ScoringFixtures.Context(
            entry: UptrendPullback(), direction: TradeDirection.Short);

        var result = _triggers.Evaluate(context, range: null);

        // Rơi xuống đường cũ, và đường cũ cũng không cho qua.
        Assert.False(result.Passed);
        Assert.NotEqual(SetupType.MaPullback, result.SetupType);
    }

    /// <summary>
    /// Cú đẩy yếu thì không xác nhận — và lý do phải HIỆN RA trong mô tả, dù trạng thái vẫn là
    /// của đường cũ.
    /// </summary>
    /// <remarks>
    /// Không cho nhánh MA ghi đè trạng thái là có chủ ý (xem chú thích ở <c>Evaluate</c>), nhưng
    /// nếu nuốt luôn lý do thì sau này không có cách nào biết vì sao nhịp hồi chẳng bao giờ chạy.
    /// </remarks>
    [Fact]
    public void Cu_day_khong_du_khoi_luong_thi_tu_choi_va_ghi_ro_ly_do()
    {
        var context = ScoringFixtures.Context(entry: UptrendPullback(impulseVolume: 1.0m));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Contains("Nhịp MA:", result.DetailVi);
        Assert.Contains("khối lượng", result.DetailVi);
    }

    [Fact]
    public void Gia_chua_hoi_ve_cham_MA_thi_tu_choi_va_ghi_ro_ly_do()
    {
        var context = ScoringFixtures.Context(entry: UptrendPullback(touchesFastMa: false));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Contains("Nhịp MA:", result.DetailVi);
        Assert.Contains("chưa hồi về chạm", result.DetailVi);
    }

    /// <summary>
    /// Bộ dò mới KHÔNG được nuốt lý do từ chối của đường cũ.
    /// </summary>
    /// <remarks>
    /// Ghép thuần cộng thêm: chỉ khi nhịp hồi XÁC NHẬN thì nó mới thắng. Không xác nhận thì phải
    /// trả về nguyên kết quả của đường cũ, nếu không mọi thống kê "chặn ở cổng nào" — thứ đang
    /// dùng để so sánh trước/sau — sẽ đứt gãy.
    /// </remarks>
    [Fact]
    public void Khong_xac_nhan_thi_tra_ve_ly_do_cua_duong_cu()
    {
        var flat = ScoringFixtures.Flat(80, price: 100m, range: 2m, volume: 100m);
        var context = ScoringFixtures.Context(entry: flat);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.NotEqual(SetupType.MaPullback, result.SetupType);
    }

    // ── Dựng chuỗi nến ──────────────────────────────────────────────────

    /// <summary>
    /// Nền phẳng → cú đẩy mạnh (MA7 cắt lên MA25) → hồi về chạm MA7.
    /// </summary>
    /// <remarks>
    /// Cây nến cuối được dựng SAU khi đã tính MA7 của chính chuỗi, để nó straddle đúng mức MA.
    /// Đặt tay một con số rồi hi vọng nó trùng MA là cách làm test đỏ vì fixture chứ không phải
    /// vì mã.
    /// </remarks>
    private static List<Candle> UptrendPullback(
        decimal impulseVolume = 2.5m, bool touchesFastMa = true, decimal topStep = 0m)
    {
        var candles = new List<Candle>();
        var i = 0;

        Candle Bar(decimal close, decimal high, decimal low, decimal volume)
        {
            var open = Start.AddMinutes(15 * i++);
            return new Candle(open, close, high, low, close, volume, open.AddMinutes(15).AddTicks(-1));
        }

        // 45 nến nền phẳng quanh 100 — đủ để MA25 ổn định và VolumeLookback có mẫu.
        for (var n = 0; n < 45; n++) candles.Add(Bar(100m, 100.4m, 99.6m, 100m));

        // Cú đẩy 8 nến lên 108, khối lượng nhân impulseVolume.
        for (var n = 1; n <= 8; n++)
        {
            var price = 100m + n;
            candles.Add(Bar(price, price + 0.4m, price - 0.4m, 100m * impulseVolume));
        }

        // Đỉnh 6 nến. Bước 0 = đi ngang, MA7 đuổi kịp và CHẠM — tức đã tiêu mất một nhịp hồi.
        // Bước > 0 = dốc lên, MA7 bị bỏ lại phía sau nên nhịp hồi sắp tới là nhịp ĐẦU TIÊN.
        // Giữ đúng 6 nến ở cả hai trường hợp để cửa sổ 10 nến dựng dừng lỗ không chạm vào cú đẩy.
        var top = 108m;
        for (var n = 0; n < 6; n++)
        {
            top += topStep;
            candles.Add(Bar(top, top + 0.4m, top - 0.4m, 100m));
        }

        // Nhịp hồi: 2 nến lùi về phía MA7. Cố ý KHÔNG xuyên hẳn qua MA rồi quay lại — làm vậy
        // sẽ thành hai sự kiện chạm tách nhau và bộ đếm chấm nó là nhịp thứ hai, đúng theo luật.
        for (var n = 1; n <= 2; n++)
        {
            var price = top - n * 0.5m;
            candles.Add(Bar(price, price + 0.3m, price - 0.3m, 100m));
        }

        // Nến cuối: dựng để straddle đúng MA7 của chuỗi (hoặc cố ý nằm trên nếu không muốn chạm).
        var ma7 = candles.TakeLast(6).Sum(c => c.Close);
        var closeGuess = candles[^1].Close;
        ma7 = (ma7 + closeGuess) / 7m;

        candles.Add(touchesFastMa
            ? Bar(ma7 + 0.1m, ma7 + 0.5m, ma7 - 0.5m, 100m)
            : Bar(ma7 + 3m, ma7 + 3.5m, ma7 + 2.5m, 100m));

        return candles;
    }
}
