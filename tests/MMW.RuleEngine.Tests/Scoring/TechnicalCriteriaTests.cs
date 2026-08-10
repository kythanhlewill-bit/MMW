using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Scoring.Criteria;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// T077 — năm tiêu chí nhóm kỹ thuật, mỗi tiêu chí ba ca: điểm tối đa, 0 điểm, thiếu dữ liệu.
/// </summary>
public class TechnicalCriteriaTests
{
    // ── technical.htf_alignment ─────────────────────────────────────────

    private static HtfAlignmentCriterion Htf() => new(ScoringFixtures.Indicators);

    [Fact]
    public void Htf_chong_EMA_thuan_chieu_duoc_diem_toi_da()
    {
        var result = Htf().Evaluate(ScoringFixtures.Context(direction: TradeDirection.Long));

        Assert.Equal(10, result.AwardedPoints);
        Assert.False(result.IsHardVeto);
    }

    [Fact]
    public void Htf_chong_EMA_nguoc_chieu_lenh_duoc_0_diem()
    {
        // Kế hoạch cho cả hai chiều nên không veto; chỉ là lệnh bán trong chồng EMA tăng.
        var result = Htf().Evaluate(ScoringFixtures.Context(direction: TradeDirection.Short));

        Assert.Equal(0, result.AwardedPoints);
        Assert.False(result.IsHardVeto);
    }

    [Fact]
    public void Htf_nguoc_ke_hoach_ngay_la_VETO_CUNG()
    {
        // Kế hoạch chỉ cho bán, nhưng khung 4h đang xếp tăng rõ — hai tầng mâu thuẫn nhau.
        var context = ScoringFixtures.Context(
            direction: TradeDirection.Short,
            plan: ScoringFixtures.Plan(AllowedDirections.ShortOnly));

        var result = Htf().Evaluate(context);

        Assert.True(result.IsHardVeto);
        Assert.Equal(VetoReason.HtfMisaligned, result.VetoReason);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public void Htf_thieu_nen_4h_thi_bao_thieu_du_lieu()
    {
        var result = Htf().Evaluate(ScoringFixtures.Context(bias: ScoringFixtures.Ramp(10)));

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── technical.market_structure ──────────────────────────────────────

    private static MarketStructureCriterion Structure() =>
        new(ScoringFixtures.Structure, ScoringFixtures.Indicators);

    [Fact]
    public void Cau_truc_khong_co_pha_vo_nao_duoc_diem_trung_tinh()
    {
        var result = Structure().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Flat(120)));

        Assert.Equal(3, result.AwardedPoints);
    }

    /// <summary>
    /// Điểm hợp lưu trượt dần theo TUỔI mẫu hình, không rơi khỏi vách ở một mốc tuỳ ý.
    /// </summary>
    /// <remarks>
    /// Đây là ràng buộc mà V1 thiếu hẳn: một cú hai đáy phá neckline lúc 09:00 vẫn cho đủ 8/10
    /// điểm cấu trúc lúc 17:00 miễn giá còn trên neckline — đúng loại lệnh vào muộn mà
    /// <c>technical.entry_location</c> sinh ra để chặn.
    ///
    /// Bơm tín hiệu thẳng vào bối cảnh thay vì dựng một chuỗi giá có mẫu hình đúng tuổi: tuổi là
    /// thứ cần đo ở đây, còn việc nhận diện mẫu hình đã có test riêng của nó.
    /// </remarks>
    [Theory]
    [InlineData(0, 8)]    // vừa hoàn thành ⟹ trọng số 1,00 ⟹ trần
    [InlineData(6, 6)]    // nửa đời ⟹ trọng số 0,50 ⟹ 3 + 2,5 làm tròn ra xa 0
    [InlineData(11, 3)]   // gần hết hạn ⟹ trọng số 0,08 ⟹ về sàn
    [InlineData(12, 3)]   // hết hạn ⟹ không còn là hợp lưu
    public void Diem_hop_luu_giam_dan_theo_tuoi_mau_hinh(int ageBars, int expected)
    {
        var context = ScoringFixtures.Context(entry: ScoringFixtures.Flat(120)) with
        {
            PriceAction = ScoringFixtures.PriceAction(doubleBottom: ageBars),
        };

        Assert.Equal(expected, Structure().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Cau_truc_pha_vo_nguoc_chieu_lenh_duoc_0_diem()
    {
        // Chuỗi răng cưa đi lên phá vỡ đỉnh xoay cũ; với lệnh bán thì phá vỡ đó là ngược chiều.
        // Phải dùng răng cưa chứ không dùng chuỗi đơn điệu: chuỗi đơn điệu không có điểm xoay
        // nào nên cũng không có phá vỡ nào để nói ngược hay thuận.
        var result = Structure().Evaluate(
            ScoringFixtures.Context(entry: ScoringFixtures.ZigZag(120), direction: TradeDirection.Short));

        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public void Cau_truc_pha_vo_va_kiem_dinh_lai_thanh_cong_duoc_diem_toi_da()
    {
        var result = Structure().Evaluate(
            ScoringFixtures.Context(entry: ScoringFixtures.BreakoutWithRetest(), direction: TradeDirection.Long));

        Assert.Equal(10, result.AwardedPoints);
        Assert.Contains("kiểm định lại thành công", result.Reason);
    }

    [Fact]
    public void Cau_truc_kiem_dinh_lai_that_bai_duoc_0_diem()
    {
        // Chuỗi răng cưa phá đỉnh rồi rơi ngay xuống dưới ở nhịp sau — setup đã hỏng, và
        // "gần đúng" ở đây không đáng một điểm nào.
        var result = Structure().Evaluate(
            ScoringFixtures.Context(entry: ScoringFixtures.ZigZag(120), direction: TradeDirection.Long));

        Assert.Equal(0, result.AwardedPoints);
        Assert.Contains("THẤT BẠI", result.Reason);
    }

    [Fact]
    public void Cau_truc_thieu_nen_thi_bao_thieu_du_lieu()
    {
        var result = Structure().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Ramp(5)));

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── technical.entry_location ────────────────────────────────────────

    private static EntryLocationCriterion Location() => new(ScoringFixtures.Indicators);

    [Fact]
    public void Vi_tri_sat_vung_gia_tri_duoc_diem_toi_da()
    {
        // Chuỗi phẳng: giá hiện tại trùng EMA20 và VWAP.
        var result = Location().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Flat(120)));

        Assert.Equal(8, result.AwardedPoints);
    }

    [Fact]
    public void Vi_tri_chay_qua_1_5_ATR_duoc_dung_0_diem_theo_FR_027()
    {
        // Đẩy giá hiện tại ra xa hẳn vùng giá trị. Biên độ nến là 2 nên ATR ≈ 2;
        // cách 100 đơn vị là 50 ATR, vượt xa trần 1.5.
        var candles = ScoringFixtures.Flat(120);
        var context = ScoringFixtures.Context(entry: candles) with { CurrentPrice = candles[^1].Close + 100m };

        var result = Location().Evaluate(context);

        Assert.Equal(0, result.AwardedPoints);
        Assert.Contains("FR-027", result.Reason);
    }

    [Fact]
    public void Vi_tri_tran_ATR_doc_tu_cau_hinh_chu_khong_viet_cung()
    {
        var candles = ScoringFixtures.Flat(120);
        var context = ScoringFixtures.Context(
            entry: candles,
            settings: ScoringFixtures.Settings(s => s.MaxAtrFromConfirmation = 100m))
            with { CurrentPrice = candles[^1].Close + 100m };

        // Cùng khoảng cách, chỉ khác trần cấu hình ⟹ không còn là 0 điểm.
        Assert.True(Location().Evaluate(context).AwardedPoints > 0);
    }

    [Fact]
    public void Vi_tri_thieu_nen_thi_bao_thieu_du_lieu()
    {
        var result = Location().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Ramp(3)));

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── technical.momentum ──────────────────────────────────────────────

    private static MomentumCriterion Momentum() => new(ScoringFixtures.Indicators);

    [Fact]
    public void Dong_luong_thuan_ca_hai_mat_duoc_diem_toi_da()
    {
        // Dải RSI mở toàn thang để test này chỉ còn phụ thuộc độ dốc biểu đồ MACD —
        // ghim dải RSI bằng cấu hình chắc chắn hơn ghim bằng cách nắn chuỗi giá.
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Accelerating(200),
            settings: ScoringFixtures.Settings(s => { s.RsiLowerBound = 0m; s.RsiUpperBound = 100m; }));

        Assert.Equal(7, Momentum().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Dong_luong_hong_ca_hai_mat_duoc_0_diem()
    {
        // Chuỗi tăng có gia tốc: RSI sát 100 (ngoài dải mặc định 45–65) và biểu đồ MACD dốc
        // LÊN — với lệnh bán thì cả hai đều ngược.
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Accelerating(200), direction: TradeDirection.Short);

        Assert.Equal(0, Momentum().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Dong_luong_chi_dung_mot_mat_duoc_diem_giua()
    {
        var context = ScoringFixtures.Context(entry: ScoringFixtures.Accelerating(200));

        // RSI ngoài dải mặc định nhưng biểu đồ MACD dốc thuận chiều mua.
        Assert.Equal(4, Momentum().Evaluate(context).AwardedPoints);
    }

    /// <summary>
    /// Hợp lưu cộng vào động lượng cũng chịu tuổi, và bằng chứng mâu thuẫn thì triệt tiêu nhau.
    /// </summary>
    /// <remarks>
    /// Cách cũ kiểm <c>Supports</c> trước rồi <c>Opposes</c> sau, nên khi cả hai cùng đúng thì
    /// bằng chứng ngược chiều bị vứt trong im lặng và một setup mâu thuẫn được cộng đủ 2 điểm y
    /// hệt một setup sạch.
    /// </remarks>
    [Theory]
    [InlineData(0, null, null, 5)]   // một mẫu hình mới tinh ⟹ +1
    [InlineData(0, 0, null, 6)]      // hai mẫu hình mới tinh ⟹ +2 (trần)
    [InlineData(11, null, null, 4)]  // gần hết hạn ⟹ trọng số 0,08 ⟹ làm tròn về 0
    [InlineData(12, null, null, 4)]  // hết hạn ⟹ không cộng gì
    [InlineData(0, null, 0, 4)]      // thuận và ngược cùng mới ⟹ triệt tiêu
    [InlineData(0, 0, 0, 5)]         // hai thuận một ngược ⟹ ròng +1
    public void Hop_luu_cong_vao_dong_luong_chiu_ca_tuoi_lan_bang_chung_nguoc(
        int supportAge, int? secondSupportAge, int? opposeAge, int expected)
    {
        // Chuỗi tăng có gia tốc với cấu hình mặc định cho điểm nền 4/7 (RSI ngoài dải, biểu đồ
        // MACD dốc thuận). Mọi chênh lệch so với 4 dưới đây đều đến từ hợp lưu.
        var context = ScoringFixtures.Context(entry: ScoringFixtures.Accelerating(200)) with
        {
            PriceAction = ScoringFixtures.PriceAction(
                doubleBottom: supportAge,
                bullishRsiDivergence: secondSupportAge,
                bearishRsiDivergence: opposeAge),
        };

        Assert.Equal(expected, Momentum().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Dong_luong_thieu_nen_thi_bao_thieu_du_lieu()
    {
        var result = Momentum().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Ramp(5)));

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public void Dong_luong_neu_so_lieu_thuc_te_trong_ly_do()
    {
        // Nguyên tắc I: "không đạt" là lý do vô dụng, "RSI 38.2 ngoài dải 45–65" thì dùng được.
        var result = Momentum().Evaluate(ScoringFixtures.Context(entry: ScoringFixtures.Accelerating(200)));

        Assert.Contains("RSI", result.Reason);
        Assert.Contains("45", result.Reason);
    }

    // ── technical.volume_confirmation ───────────────────────────────────

    private static VolumeConfirmationCriterion Volume() => new(ScoringFixtures.Indicators);

    [Fact]
    public void Khoi_luong_vuot_nguong_pha_vo_duoc_diem_toi_da()
    {
        var candles = ScoringFixtures.Flat(60);
        candles[^1] = candles[^1] with { Open = candles[^1].Close - 1m, Volume = 100m * 2m };

        Assert.Equal(5, Volume().Evaluate(ScoringFixtures.Context(entry: candles)).AwardedPoints);
    }

    [Fact]
    public void Khoi_luong_duoi_trung_binh_duoc_0_diem()
    {
        var candles = ScoringFixtures.Flat(60);
        candles[^1] = candles[^1] with { Volume = 10m };

        Assert.Equal(0, Volume().Evaluate(ScoringFixtures.Context(entry: candles)).AwardedPoints);
    }

    [Fact]
    public void Khoi_luong_nguong_pha_vo_doc_tu_cau_hinh()
    {
        var candles = ScoringFixtures.Flat(60);
        candles[^1] = candles[^1] with { Open = candles[^1].Close - 1m, Volume = 120m };

        var strict = ScoringFixtures.Context(entry: candles);
        var lenient = ScoringFixtures.Context(
            entry: candles, settings: ScoringFixtures.Settings(s => s.VolumeBreakoutMultiple = 1.1m));

        Assert.Equal(3, Volume().Evaluate(strict).AwardedPoints);
        Assert.Equal(5, Volume().Evaluate(lenient).AwardedPoints);
    }

    [Fact]
    public void Volume_lon_nhung_nen_nguoc_chieu_khong_duoc_5_diem()
    {
        var candles = ScoringFixtures.Flat(60);
        candles[^1] = candles[^1] with { Open = candles[^1].Close + 1m, Volume = 200m };

        Assert.Equal(2, Volume().Evaluate(ScoringFixtures.Context(entry: candles)).AwardedPoints);
    }

    [Fact]
    public void Volume_breakout_hai_nen_truoc_van_xac_nhan_diem_vao_retest()
    {
        var candles = ScoringFixtures.Flat(60);
        candles[^3] = candles[^3] with
        {
            Open = candles[^3].Close - 1m,
            Volume = 200m,
        };

        var result = Volume().Evaluate(ScoringFixtures.Context(entry: candles));

        Assert.Equal(5, result.AwardedPoints);
        Assert.Contains("cách 2 nến", result.Reason);
    }

    [Fact]
    public void Khoi_luong_khong_co_nen_thi_bao_thieu_du_lieu()
    {
        var result = Volume().Evaluate(ScoringFixtures.Context(entry: Array.Empty<Application.MarketData.Models.Candle>()));

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }
}
