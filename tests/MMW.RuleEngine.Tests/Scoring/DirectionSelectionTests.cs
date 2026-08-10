using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// V2 §4 — chọn chiều bằng vị trí trong biên độ và bằng biên điểm, không bằng EMA.
/// </summary>
/// <remarks>
/// Engine cũ chọn chiều trên ngày <c>Both</c> bằng một phép so EMA 20/50 khung 4 giờ rồi không
/// bao giờ xét chiều còn lại. Trên ngày đi ngang — đúng loại ngày mà kế hoạch cho cả hai chiều —
/// hai đường EMA đó đan xen, nên chiều lệnh về bản chất là tung đồng xu có trả phí.
/// </remarks>
public class DirectionSelectionTests
{
    private static DirectionPolicy Policy() => new(ScoringFixtures.Swings);

    // ── Đọc biên độ ─────────────────────────────────────────────────────

    /// <summary>
    /// Biên độ dựng từ pivot ĐÃ XÁC NHẬN trên khung thiên hướng, và vị trí là phần trăm tuyến tính.
    /// </summary>
    [Fact]
    public void Vi_tri_trong_bien_do_do_bang_pivot_da_xac_nhan()
    {
        var candles = ZigZag4h();

        var location = Policy().Locate(candles, pivotBars: 2, price: 0m + Mid(candles));

        Assert.NotNull(location);
        Assert.True(location!.Low < location.High);
        Assert.InRange(location.Percent, 40m, 60m);
    }

    [Fact]
    public void Gia_ngoai_bien_do_cho_ra_phan_tram_ngoai_0_100_chu_khong_bi_ket()
    {
        // KHÔNG kẹp về [0, 100]: "sát biên trên" và "đã phá lên khỏi biên" là hai kết luận khác
        // hẳn nhau, và kẹp lại sẽ biến cú phá vỡ thành một tín hiệu fade.
        var candles = ZigZag4h();
        var location = Policy().Locate(candles, pivotBars: 2, price: 100_000m);

        Assert.NotNull(location);
        Assert.True(location!.Percent > 100m);
    }

    [Fact]
    public void Khong_du_diem_xoay_thi_khong_doc_duoc_bien_do()
    {
        var flat = ScoringFixtures.Flat(40, interval: TimeSpan.FromHours(4));

        Assert.Null(Policy().Locate(flat, pivotBars: 2, price: 1000m));
        Assert.Null(Policy().Locate(Array.Empty<Candle>(), pivotBars: 2, price: 1000m));
    }

    // ── Ràng buộc vị trí trên ngày đi ngang ─────────────────────────────

    [Fact]
    public void Ngay_range_giua_bien_do_thi_khong_chieu_nao_duoc_phep()
    {
        var candles = ZigZag4h();
        var result = Candidates(candles, Mid(candles));

        Assert.Empty(result.Allowed);
        Assert.Equal(VetoReason.NotAtRangeEdge, result.Veto);
        Assert.Contains("nằm giữa biên độ", result.Detail);
    }

    [Fact]
    public void Sat_bien_tren_chi_cho_ban_va_sat_bien_duoi_chi_cho_mua()
    {
        var candles = ZigZag4h();
        var bounds = Policy().Locate(candles, 2, Mid(candles))!;
        var span = bounds.High - bounds.Low;

        var atTop = Candidates(candles, bounds.Low + span * 0.9m);
        var atBottom = Candidates(candles, bounds.Low + span * 0.1m);

        Assert.Equal(new[] { TradeDirection.Short }, atTop.Allowed);
        Assert.Equal(new[] { TradeDirection.Long }, atBottom.Allowed);
        Assert.Null(atTop.Veto);
        Assert.Null(atBottom.Veto);

        // Chiều bị loại vẫn được nêu tên: nó sẽ được chấm để ghi vào phiếu, dù không tham gia
        // quyết định. Không có nó thì không ai kiểm được quy tắc biên độ về sau.
        Assert.Equal(new[] { TradeDirection.Long }, atTop.ExcludedOrEmpty);
        Assert.Equal(new[] { TradeDirection.Short }, atBottom.ExcludedOrEmpty);
    }

    [Fact]
    public void Gia_da_pha_ra_ngoai_bien_do_thi_khong_phai_co_hoi_fade()
    {
        var candles = ZigZag4h();
        var bounds = Policy().Locate(candles, 2, Mid(candles))!;

        var result = Candidates(candles, bounds.High + (bounds.High - bounds.Low));

        Assert.Empty(result.Allowed);
        Assert.Equal(VetoReason.NotAtRangeEdge, result.Veto);
        Assert.Contains("vượt lên trên biên độ", result.Detail);
    }

    [Fact]
    public void Nguong_vung_bien_doc_tu_cau_hinh()
    {
        var candles = ZigZag4h();
        var bounds = Policy().Locate(candles, 2, Mid(candles))!;
        var price = bounds.Low + (bounds.High - bounds.Low) * 0.65m;

        var strict = Candidates(candles, price, s => s.RangeEdgePercent = 25m);
        var lenient = Candidates(candles, price, s => s.RangeEdgePercent = 40m);

        Assert.Empty(strict.Allowed);
        Assert.Equal(new[] { TradeDirection.Short }, lenient.Allowed);
    }

    /// <summary>
    /// Ngày KHÔNG đi ngang thì vị trí trong biên độ không được nói gì.
    /// </summary>
    /// <remarks>
    /// Trên ngày xu hướng, "biên" của một biên độ đang bị phá vỡ chính là chỗ nên vào chứ không
    /// phải chỗ nên fade — và chiều đã bị kế hoạch ngày khoá lại từ trước rồi.
    /// </remarks>
    [Fact]
    public void Ngay_trend_khong_chiu_rang_buoc_vi_tri()
    {
        var candles = ZigZag4h();

        var result = Policy().Candidates(
            ScoringFixtures.Plan(AllowedDirections.LongOnly, DayRegime.TrendUp),
            ScoringFixtures.Settings(),
            candles,
            Mid(candles));

        Assert.Equal(new[] { TradeDirection.Long }, result.Allowed);
        Assert.Null(result.Veto);
        Assert.Null(result.Range);
    }

    [Fact]
    public void Ngay_range_khong_doc_duoc_bien_do_thi_khong_giao_dich()
    {
        var flat = ScoringFixtures.Flat(40, interval: TimeSpan.FromHours(4));
        var result = Candidates(flat, 1000m);

        Assert.Empty(result.Allowed);
        Assert.Equal(VetoReason.InsufficientData, result.Veto);
    }

    [Fact]
    public void Ke_hoach_ngay_khong_cho_chieu_nao_thi_dung_lai_ngay()
    {
        var result = Policy().Candidates(
            ScoringFixtures.Plan(AllowedDirections.None, DayRegime.Range),
            ScoringFixtures.Settings(),
            ZigZag4h(),
            1000m);

        Assert.Empty(result.Allowed);
        Assert.Equal(VetoReason.DirectionNotAllowed, result.Veto);
    }

    // ── Biên chọn chiều ─────────────────────────────────────────────────

    [Fact]
    public void Chenh_lech_du_bien_thi_chon_chieu_cao_diem_hon()
    {
        var choice = DirectionSelector.Select(
            new[] { Scored(TradeDirection.Long, 40), Scored(TradeDirection.Short, 30) });

        Assert.Equal(TradeDirection.Long, choice.Direction);
        Assert.Equal(10, choice.Margin);
        Assert.Equal(30, choice.OppositeScore!.DirectionalScore);
    }

    /// <summary>
    /// Sau A/B #23/#24, chênh nhỏ vẫn chọn chiều cao điểm hơn; không còn gate không tạo giá trị.
    /// </summary>
    [Fact]
    public void Chenh_lech_nho_van_chon_chieu_cao_hon_va_chi_ghi_margin_de_chan_doan()
    {
        var choice = DirectionSelector.Select(
            new[] { Scored(TradeDirection.Long, 40), Scored(TradeDirection.Short, 36) });

        Assert.Equal(TradeDirection.Long, choice.Direction);
        Assert.Equal(4, choice.Margin);
        Assert.Contains("chênh 4", choice.ReasonVi);
    }

    [Fact]
    public void Hoa_diem_luon_ra_cung_mot_ket_qua_tat_dinh()
    {
        var forward = DirectionSelector.Select(
            new[] { Scored(TradeDirection.Long, 33), Scored(TradeDirection.Short, 33) });
        var reversed = DirectionSelector.Select(
            new[] { Scored(TradeDirection.Short, 33), Scored(TradeDirection.Long, 33) });

        Assert.Equal(forward.Direction, reversed.Direction);
        Assert.Equal(0, forward.Margin);
        Assert.Equal(0, reversed.Margin);
    }

    /// <summary>
    /// Chiều bị veto cứng bị LOẠI khỏi cuộc so, không phải bị coi là 0 điểm.
    /// </summary>
    /// <remarks>
    /// Veto cứng làm vòng chấm dừng giữa chừng, nên điểm của chiều đó là một tổng dở dang. Đem nó
    /// vào phép trừ là so hai con số không cùng đơn vị — và nó sẽ cho chiều còn lại một biên rộng
    /// bịa đặt.
    /// </remarks>
    [Fact]
    public void Chieu_bi_veto_bi_loai_chu_khong_duoc_coi_la_0_diem()
    {
        var choice = DirectionSelector.Select(
            new[] { Vetoed(TradeDirection.Long), Scored(TradeDirection.Short, 5) });

        Assert.Equal(TradeDirection.Short, choice.Direction);
        Assert.Null(choice.Margin);
        Assert.Null(choice.OppositeScore);
    }

    [Fact]
    public void Moi_chieu_deu_bi_veto_thi_giu_nguyen_ly_do_cua_chieu_dau_tien()
    {
        var choice = DirectionSelector.Select(
            new[] { Vetoed(TradeDirection.Long), Vetoed(TradeDirection.Short) });

        Assert.Equal(TradeDirection.Long, choice.Direction);
        Assert.True(choice.Score.IsVetoed);
        Assert.Null(choice.OppositeScore);
    }

    [Fact]
    public void Mot_ung_vien_duy_nhat_thi_khong_co_bien_nao_de_doi_hoi()
    {
        var choice = DirectionSelector.Select(new[] { Scored(TradeDirection.Short, 1) });

        Assert.Equal(TradeDirection.Short, choice.Direction);
        Assert.Null(choice.Margin);
    }

    // ── Bộ dựng ─────────────────────────────────────────────────────────

    private static DirectionCandidates Candidates(
        IReadOnlyList<Candle> biasCandles, decimal price, Action<EngineSetting>? configure = null) =>
        Policy().Candidates(
            ScoringFixtures.Plan(AllowedDirections.Both, DayRegime.Range),
            ScoringFixtures.Settings(configure),
            biasCandles,
            price);

    /// <summary>Nến 4h răng cưa: đủ điểm xoay hai phía để dựng được một biên độ thật.</summary>
    private static List<Candle> ZigZag4h() =>
        ScoringFixtures.ZigZag(40, driftPerCycle: 0m, interval: TimeSpan.FromHours(4));

    private static decimal Mid(IReadOnlyList<Candle> candles)
    {
        var bounds = Policy().Locate(candles, 2, candles[^1].Close)!;
        return (bounds.Low + bounds.High) / 2m;
    }

    private static (TradeDirection, ScoringOutcome) Scored(TradeDirection direction, int directional) =>
        (direction, new ScoringOutcome(
            TotalScore: directional + 20,
            TechnicalScore: 0, MarketScore: 0, LiquidityScore: 0, DisciplinePenalty: 0,
            IsVetoed: false, VetoReason: null, VetoDetail: null,
            Lines: Array.Empty<ScoredLine>(),
            DirectionalScore: directional,
            DirectionalMaxPoints: 59));

    private static (TradeDirection, ScoringOutcome) Vetoed(TradeDirection direction) =>
        (direction, new ScoringOutcome(
            TotalScore: 0,
            TechnicalScore: 0, MarketScore: 0, LiquidityScore: 0, DisciplinePenalty: 0,
            IsVetoed: true, VetoReason: VetoReason.InsufficientRoom, VetoDetail: "test",
            Lines: Array.Empty<ScoredLine>(),
            DirectionalScore: 0,
            DirectionalMaxPoints: 59));
}
