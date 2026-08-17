using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Scoring.Criteria;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// Dừng lỗ và mục tiêu phải neo vào CẤU TRÚC GIÁ, không phải vào một bội ATR cố định.
/// </summary>
/// <remarks>
/// Công thức cũ đặt dừng lỗ ở đúng <c>giá ± 1,5 × ATR</c>, mù hoàn toàn với đáy xoay gần nhất.
/// Khi đáy đó nằm cách giá 1,2 ATR thì dừng lỗ 1,5 ATR rơi ngay DƯỚI nơi lệnh dừng của số đông
/// đang nằm — chỗ giá bị hút tới. Setup đúng, hướng đúng, thua vì dừng lỗ đặt sai vài chục điểm
/// giá.
/// </remarks>
public class StructuralLevelTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly IStructuralLevelPlanner Planner = new StructuralLevelPlanner(new SwingDetector());

    private static EngineSetting Settings(Action<EngineSetting>? configure = null)
    {
        var s = EngineSettingDefaults.Create(1);
        configure?.Invoke(s);
        return s;
    }

    /// <summary>
    /// Dựng nến từ một đường giá, biên độ ±<paramref name="halfRange"/> quanh mỗi mức.
    /// </summary>
    /// <remarks>
    /// Mặc định biên độ bằng 0 để giá điểm xoay ĐÚNG BẰNG con số trong đường giá. Bộ phát hiện
    /// điểm xoay đọc <c>High</c>/<c>Low</c> chứ không đọc <c>Close</c>, nên biên độ khác 0 làm
    /// mọi mức mong đợi lệch đi đúng nửa biên độ — và test sẽ đỏ vì fixture chứ không phải vì mã.
    /// </remarks>
    private static List<Candle> Path(IReadOnlyList<decimal> closes, decimal halfRange = 0m) =>
        closes.Select((c, i) =>
        {
            var open = Start.AddMinutes(15 * i);
            return new Candle(open, c, c + halfRange, c - halfRange, c, 100m, open.AddMinutes(15).AddTicks(-1));
        }).ToList();

    private static StructuralLevelRequest Request(
        IReadOnlyList<Candle> entry,
        decimal price,
        decimal atr,
        TradeDirection direction = TradeDirection.Long,
        EngineSetting? settings = null) => new()
        {
            Entry = price,
            Direction = direction,
            Atr = atr,
            Settings = settings ?? Settings(),
            EntryCandles = entry,
        };

    // ── Dừng lỗ ─────────────────────────────────────────────────────────

    /// <summary>
    /// Dừng lỗ phải nằm NGOÀI đáy xoay, không phải ở một khoảng cách ATR bất kỳ.
    /// </summary>
    [Fact]
    public void Dung_lo_dat_ngoai_day_xoay_chu_khong_theo_boi_ATR_co_dinh()
    {
        // Đáy xoay rõ ràng tại 90, giá hiện tại 100, ATR 4.
        // Công thức cũ: 100 − 1,5×4 = 94 — nằm TRÊN đáy 90, tức trong vùng nhiễu bình thường.
        // V2: 90 − 0,3×4 = 88,8 — nằm dưới đáy, đúng chỗ setup bị phủ định.
        var candles = Path(new decimal[]
        {
            100, 99, 98, 97, 96, 95, 94, 93, 92, 90,   // đáy xoay tại chỉ số 9
            92, 94, 96, 98, 99, 100, 100, 100, 100, 100,
        });

        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m));

        Assert.NotNull(levels);
        Assert.True(levels!.StopIsStructural);
        Assert.Equal(88.8m, levels.StopLoss);

        // Và khoảng cách thực tế rộng hơn hẳn 1,5 ATR — chính là thứ cắt chi phí tính theo R.
        Assert.True(levels.StopAtrMultiple > 1.5m,
            $"Dừng lỗ cấu trúc phải rộng hơn 1,5 ATR, thực tế {levels.StopAtrMultiple:N2}.");
    }

    /// <summary>
    /// Cấu trúc quá GẦN thì nới ra tới sàn — dừng lỗ dính sát giá bị nhiễu quét.
    /// </summary>
    [Fact]
    public void Cau_truc_qua_gan_thi_noi_dung_lo_ra_toi_san()
    {
        // Đáy xoay tại 99,5 trong khi giá 100 và ATR 4 ⟹ khoảng cách cấu trúc chỉ 0,7/4 ATR.
        var candles = Path(new decimal[]
        {
            100, 100, 100, 100, 100, 99.5m, 100, 100, 100, 100,
            100, 100, 100, 100, 100, 100, 100, 100, 100, 100,
        });

        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m,
            settings: Settings(s => s.StopAtrMultipleMin = 1.0m)));

        Assert.NotNull(levels);
        Assert.Equal(1.0m, levels!.StopAtrMultiple);
        Assert.Equal(96m, levels.StopLoss);
    }

    /// <summary>
    /// Cấu trúc quá XA thì KHÔNG vào lệnh — co size không sửa được việc không đọc được cấu trúc.
    /// </summary>
    [Fact]
    public void Cau_truc_qua_xa_thi_khong_vao_lenh_chu_khong_co_size()
    {
        // Đáy xoay tại 60, giá 100, ATR 4 ⟹ 10 ATR, vượt xa trần 3 ATR.
        var candles = Path(new decimal[]
        {
            100, 95, 90, 85, 80, 75, 70, 65, 60,   // đáy xoay tại chỉ số 8
            65, 70, 75, 80, 85, 90, 95, 100, 100, 100, 100,
        });

        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m,
            settings: Settings(s => s.StopAtrMultipleMax = 3.0m)));

        Assert.Null(levels);
    }

    /// <summary>Không có điểm xoay nào thì lùi về công thức ATR và ĐÁNH DẤU là dự phòng.</summary>
    [Fact]
    public void Khong_co_diem_xoay_thi_lui_ve_cong_thuc_ATR_va_danh_dau()
    {
        // Chuỗi tăng đơn điệu: không nến nào là cực trị địa phương ⟹ không có điểm xoay.
        var candles = Path(Enumerable.Range(0, 40).Select(i => 100m + i).ToList());

        var levels = Planner.Plan(Request(candles, price: 139m, atr: 4m));

        Assert.NotNull(levels);
        Assert.False(levels!.StopIsStructural);
        Assert.Equal(139m - 1.5m * 4m, levels.StopLoss);
    }

    /// <summary>Chiều bán là ảnh gương đúng nghĩa: dừng lỗ nằm TRÊN đỉnh xoay.</summary>
    [Fact]
    public void Lenh_ban_dat_dung_lo_tren_dinh_xoay()
    {
        var candles = Path(new decimal[]
        {
            100, 101, 102, 103, 104, 105, 106, 107, 108, 110,   // đỉnh xoay tại chỉ số 9
            108, 106, 104, 102, 101, 100, 100, 100, 100, 100,
        });

        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m, direction: TradeDirection.Short));

        Assert.NotNull(levels);
        Assert.True(levels!.StopIsStructural);
        Assert.Equal(111.2m, levels.StopLoss);   // 110 + 0,3 × 4
    }

    // ── Mục tiêu và tỉ lệ lãi/lỗ ────────────────────────────────────────

    /// <summary>
    /// Mục tiêu lấy mức cấu trúc đối diện gần nhất, lùi vào 0,2 ATR.
    /// </summary>
    /// <remarks>
    /// Chốt lời phải đứng TRƯỚC hàng người đang chờ ở mức đó, không đứng cùng hàng. Chênh lệch
    /// 0,2 ATR là khác biệt giữa "khớp đủ" và "chạm rồi quay đầu".
    /// </remarks>
    [Fact]
    public void Muc_tieu_dung_truoc_muc_khang_cu_gan_nhat()
    {
        var candles = Path(new decimal[]
        {
            100, 102, 104, 106, 108, 120, 108, 106, 104, 102,   // đỉnh xoay tại 120 (chỉ số 5)
            100, 98, 96, 94, 92, 90, 92, 94, 96, 98,            // đáy xoay tại 90 (chỉ số 15)
            100, 100, 100, 100, 100,
        });

        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m));

        Assert.NotNull(levels);
        Assert.True(levels!.TargetIsStructural);
        Assert.Equal(120m - 0.2m * 4m, levels.TakeProfit);   // 119,2
    }

    [Fact]
    public void Khong_co_muc_doi_dien_thi_dung_boi_R_du_phong_va_danh_dau()
    {
        var candles = Path(new decimal[]
        {
            100, 99, 98, 97, 96, 95, 94, 93, 92, 90,
            92, 94, 96, 98, 99, 100, 100, 100, 100, 100,
        });

        var request = Request(candles, price: 100m, atr: 4m) with { FallbackRiskReward = 2.0m };
        var levels = Planner.Plan(request);

        Assert.NotNull(levels);
        Assert.False(levels!.TargetIsStructural);
        Assert.Equal(2.0m, levels.RiskReward);
    }

    [Fact]
    public void TP1_bo_qua_can_qua_gan_va_lay_muc_dau_tien_tu_12R()
    {
        var entry = Path(new decimal[]
        {
            100, 104, 108, 104, 100, 96, 90, 96, 100, 100, 100, 100,
        });
        var bias = Path(new decimal[]
        {
            100, 105, 110, 115, 110, 105, 100,
            110, 120, 130, 120, 110, 100,
        });
        var request = Request(entry, price: 100m, atr: 4m) with { BiasCandles = bias };

        var levels = Planner.Plan(request);

        Assert.NotNull(levels);
        Assert.Equal(114.2m, levels!.FirstTakeProfit);
        Assert.Equal(129.2m, levels.TakeProfit);
        Assert.Equal(129.2m, levels.RunnerTakeProfit);
        Assert.True(levels.RiskReward >= 1.6m);
        Assert.InRange(levels.FirstTargetRiskReward!.Value, 1.2m, 1.6m);
    }

    [Fact]
    public void Khong_co_can_thi_fallback_khong_tu_mau_thuan_voi_rao_cau_hinh()
    {
        var candles = Path(Enumerable.Range(0, 40).Select(i => 100m + i).ToList());
        var request = Request(candles, price: 139m, atr: 4m) with { FallbackRiskReward = 1.5m };

        var levels = Planner.Plan(request);

        Assert.NotNull(levels);
        Assert.Equal(1.6m, levels!.RiskReward);
    }

    // ── Rào technical.structural_room ───────────────────────────────────

    /// <summary>
    /// R:R cấu trúc dưới ngưỡng ⟹ veto cứng, không phải điểm thấp.
    /// </summary>
    /// <remarks>
    /// Đây là hệ quả trực tiếp của toán chi phí. Với phí taker hai chiều cộng trượt giá, một
    /// lệnh thua tốn khoảng 1,2–1,5R còn một lệnh thắng tại 1R chỉ thu về 0,6–0,8R — tỉ lệ thắng
    /// hoà vốn ở mục tiêu 1R là khoảng 72%. Vào lệnh như thế không phải chấp nhận rủi ro, mà là
    /// trả phí để tung đồng xu.
    /// </remarks>
    [Fact]
    public void Khong_du_cho_chay_thi_veto_cung_chu_khong_cham_diem_thap()
    {
        var context = ScoringFixtures.Context() with
        {
            StructuralLevels = new StructuralLevels(
                StopLoss: 98m, TakeProfit: 101m, RiskReward: 1.2m,
                StopIsStructural: true, TargetIsStructural: true,
                StopAtrMultiple: 2m, ReasonVi: "test"),
        };

        var result = new StructuralRoomCriterion().Evaluate(context);

        Assert.True(result.IsHardVeto);
        Assert.Equal(VetoReason.InsufficientRoom, result.VetoReason);
    }

    [Fact]
    public void Du_cho_chay_thi_cho_qua_voi_0_diem()
    {
        var result = new StructuralRoomCriterion().Evaluate(ScoringFixtures.Context());

        Assert.False(result.IsHardVeto);
        Assert.Equal(0, result.AwardedPoints);
        Assert.True(result.DataAvailable);
    }

    /// <summary>
    /// Không dựng được mức vì cấu trúc quá xa là một KẾT LUẬN, không phải thiếu dữ liệu.
    /// </summary>
    /// <remarks>
    /// Phân biệt hai thứ này quan trọng với thống kê "vì sao hôm nay đứng ngoài": gộp chung sẽ
    /// trộn lẫn lỗi hạ tầng với quyết định giao dịch, và câu trả lời phổ biến nhất sẽ vô nghĩa.
    /// </remarks>
    [Fact]
    public void Cau_truc_qua_xa_la_veto_con_thieu_nen_la_thieu_du_lieu()
    {
        var tooFar = ScoringFixtures.Context() with { StructuralLevels = null };
        var starved = ScoringFixtures.Starved();

        var vetoed = new StructuralRoomCriterion().Evaluate(tooFar);
        var missing = new StructuralRoomCriterion().Evaluate(starved);

        Assert.True(vetoed.IsHardVeto);
        Assert.Equal(VetoReason.InsufficientRoom, vetoed.VetoReason);

        Assert.False(missing.IsHardVeto);
        Assert.False(missing.DataAvailable);
    }

    /// <summary>Rào 0 điểm không được làm xê dịch thang 80.</summary>
    [Fact]
    public void Rao_0_diem_khong_lam_xe_dich_thang_diem()
    {
        var outcome = new EntryScorer(ScoringFixtures.AllCriteria()).Score(ScoringFixtures.Context());

        Assert.Equal(80, outcome.TotalMaxPoints);
    }

    // ── Sàn dừng lỗ theo phần trăm giá ──────────────────────────────────

    /// <summary>
    /// Thị trường bất động: sàn ATR co lại theo, sàn phần trăm phải giữ dừng lỗ khỏi dính sát giá.
    /// </summary>
    /// <remarks>
    /// Đây là lỗ hổng đã trả giá bằng dữ liệu thật tuần 10–17/08/2026. ATR ở phân vị 1–2 nên
    /// <c>StopAtrMultipleMin × ATR</c> chỉ còn vài phần vạn, và bộ dựng mức cho ra dừng lỗ
    /// <b>1–7 bps</b>. Ở bề rộng đó, phí một vòng lệnh ăn 1,5–9,6R — tức gấp nhiều lần số tiền
    /// đem ra mạo hiểm.
    /// </remarks>
    [Fact]
    public void Thi_truong_phang_thi_san_phan_tram_giu_dung_lo()
    {
        // Đáy xoay chỉ cách giá 0,05% (1885 → 1884). ATR 0,5 nên sàn ATR chỉ 0,5 (≈0,027%).
        var candles = Path(new decimal[]
        {
            1885, 1884.8m, 1884.6m, 1884.4m, 1884.2m, 1884,   // đáy xoay tại chỉ số 5
            1884.2m, 1884.4m, 1884.6m, 1884.8m, 1885, 1885, 1885, 1885, 1885,
        });

        var settings = Settings(s =>
        {
            s.MinStopDistancePercent = 0.40m;
            s.StopAtrMultipleMax = 20m;      // nới trần để cô lập đúng tác dụng của SÀN
        });

        var levels = Planner.Plan(Request(candles, price: 1885m, atr: 0.5m, settings: settings));

        Assert.NotNull(levels);
        var distancePercent = (1885m - levels!.StopLoss) / 1885m * 100m;
        Assert.True(distancePercent >= 0.40m,
            $"Dừng lỗ phải cách ít nhất 0,40%, thực tế {distancePercent:N4}%.");
    }

    /// <summary>Sàn ATR vẫn thắng khi thị trường động — sàn phần trăm chỉ là mức tối thiểu.</summary>
    [Fact]
    public void Thi_truong_dong_thi_san_ATR_van_thang()
    {
        var candles = Path(new decimal[]
        {
            100, 99, 98, 97, 96, 95, 94, 93, 92, 90,
            92, 94, 96, 98, 99, 100, 100, 100, 100, 100,
        });

        // 0,4% của 100 chỉ là 0,4 — nhỏ hơn hẳn cấu trúc thật (đáy 90 + đệm). Không được đụng vào.
        var levels = Planner.Plan(Request(candles, price: 100m, atr: 4m,
            settings: Settings(s => s.MinStopDistancePercent = 0.40m)));

        Assert.NotNull(levels);
        Assert.Equal(88.8m, levels!.StopLoss);
        Assert.True(levels.StopIsStructural);
    }

    /// <summary>
    /// Sàn cao hơn trần thì KHÔNG vào lệnh — nới rồi vẫn không hợp lệ là câu trả lời "đứng ngoài".
    /// </summary>
    /// <remarks>
    /// Đúng ý định: cấu trúc nhỏ tới mức không thể vừa nằm trong trần ATR vừa đủ rộng để trả phí
    /// thì không có lệnh nào đáng vào. Đây là cách sàn này làm hệ thống giao dịch ÍT đi, không
    /// phải nhiều lên.
    /// </remarks>
    [Fact]
    public void San_vuot_tran_thi_khong_dung_duoc_muc_nao()
    {
        var candles = Path(new decimal[]
        {
            1885, 1884.8m, 1884.6m, 1884.4m, 1884.2m, 1884,
            1884.2m, 1884.4m, 1884.6m, 1884.8m, 1885, 1885, 1885, 1885, 1885,
        });

        // ATR 0,5 ⟹ trần 3 ATR = 1,5 (≈0,08%). Sàn 0,40% = 7,54. Sàn > trần.
        var levels = Planner.Plan(Request(candles, price: 1885m, atr: 0.5m,
            settings: Settings(s => s.MinStopDistancePercent = 0.40m)));

        Assert.Null(levels);
    }

    /// <summary>Đặt sàn về 0 thì hành vi quay lại y như trước — không khoá cứng con số nào.</summary>
    [Fact]
    public void San_bang_0_thi_giu_nguyen_hanh_vi_cu()
    {
        var candles = Path(new decimal[]
        {
            1885, 1884.8m, 1884.6m, 1884.4m, 1884.2m, 1884,
            1884.2m, 1884.4m, 1884.6m, 1884.8m, 1885, 1885, 1885, 1885, 1885,
        });

        var levels = Planner.Plan(Request(candles, price: 1885m, atr: 0.5m,
            settings: Settings(s => s.MinStopDistancePercent = 0m)));

        Assert.NotNull(levels);
        Assert.True((1885m - levels!.StopLoss) / 1885m * 100m < 0.40m);
    }
}
