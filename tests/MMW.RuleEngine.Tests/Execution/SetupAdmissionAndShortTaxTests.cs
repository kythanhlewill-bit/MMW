using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Sizing;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Hai bộ lọc lấy từ kết cục đo được: cấm setup thua và đánh thuế điểm lên chiều bán khống.
/// </summary>
/// <remarks>
/// Cả hai đều là quyết định của người vận hành dựa trên số liệu, không phải phát hiện của thuật
/// toán. Vì thế cả hai đều nằm trong cấu hình và đều phải TẮT ĐƯỢC — test dưới đây khoá cả chiều
/// bật lẫn chiều tắt, để một ngày có mẫu lớn hơn nói ngược lại thì việc gỡ ra là sửa cấu hình
/// chứ không phải sửa mã.
/// </remarks>
public sealed class SetupAdmissionAndShortTaxTests
{
    private readonly StrategyAdmissionPolicy _admission = new();
    private readonly ScoreBasedPositionSizer _sizer = new();

    private static ScoringOutcome Score(int total) => new(
        total, total, 0, 0, 0, false, null, null, Array.Empty<ScoredLine>());

    private static SetupTriggerDecision Trigger(SetupType setup) =>
        new(true, setup, SetupTriggerState.Confirmed, "xác nhận", SetupQualityScore: 80);

    private static readonly DateTime Monday = new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    // ── Cấm setup theo cấu hình ─────────────────────────────────────────

    /// <remarks>
    /// Ba setup mặc định bị cấm đều có sim net R âm VÀ 0 thắng trên lệnh thật — hai nguồn độc
    /// lập cùng chiều. Xem <c>EngineSetting.DisabledSetupTypes</c> cho bảng số.
    /// </remarks>
    [Theory]
    [InlineData(SetupType.RectangleRangeFade)]
    [InlineData(SetupType.RectangleBreakout)]
    [InlineData(SetupType.TriangleBreakout)]
    public void Setup_nam_trong_danh_sach_cam_thi_bi_tu_choi(SetupType setup)
    {
        var settings = ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6);

        var decision = _admission.Evaluate(
            settings.StrategyVersion, Trigger(setup), Score(70), Monday, settings);

        Assert.False(decision.Passed);
        Assert.Contains("DisabledSetupTypes", decision.DetailVi);
    }

    /// <remarks>
    /// MaDeepPullback là nguồn R dương duy nhất của cả đợt (+14,25R trên 10 lệnh). Cấm nhầm nó
    /// là hỏng đúng thứ đang hoạt động, nên nó có test riêng chứ không nằm chung bảng trên.
    /// </remarks>
    [Fact]
    public void Setup_khong_bi_cam_thi_van_qua()
    {
        var settings = ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6);

        var decision = _admission.Evaluate(
            settings.StrategyVersion, Trigger(SetupType.MaDeepPullback), Score(70), Monday, settings);

        Assert.True(decision.Passed);
    }

    [Fact]
    public void Danh_sach_cam_rong_thi_khong_cam_gi()
    {
        var settings = ScoringFixtures.Settings(s =>
        {
            s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6;
            s.DisabledSetupTypes = string.Empty;
        });

        var decision = _admission.Evaluate(
            settings.StrategyVersion, Trigger(SetupType.TriangleBreakout), Score(70), Monday, settings);

        Assert.True(decision.Passed);
    }

    /// <summary>
    /// Không truyền cấu hình thì admission giữ nguyên hành vi cũ.
    /// </summary>
    /// <remarks>
    /// Tham số <c>settings</c> là tuỳ chọn để không phá các nơi gọi cũ. Ranh giới ấy phải được
    /// khoá lại: nếu một ngày nào đó thiếu cấu hình mà lại thành "cấm hết", cả engine sẽ im lặng
    /// dừng vào lệnh và triệu chứng trông y hệt một ngày không có setup nào.
    /// </remarks>
    [Fact]
    public void Khong_co_cau_hinh_thi_khong_cam_setup_nao()
    {
        var decision = _admission.Evaluate(
            TradingStrategyVersion.AdaptiveSidewaysV6, Trigger(SetupType.TriangleBreakout), Score(70), Monday);

        Assert.True(decision.Passed);
    }

    /// <summary>Danh sách cấm nhận cả tên lẫn số, và tên gõ sai phải báo lỗi cấu hình.</summary>
    [Fact]
    public void Danh_sach_cam_nhan_ca_ten_lan_so_va_bat_ten_go_sai()
    {
        var byNumber = EngineSetting.ParseDisabledSetups("6,7", out var noneInvalid);
        Assert.Contains(SetupType.RectangleBreakout, byNumber);
        Assert.Contains(SetupType.TriangleBreakout, byNumber);
        Assert.Empty(noneInvalid);

        EngineSetting.ParseDisabledSetups("RectangleBreakout,KhongCoSetupNayDau", out var invalid);
        Assert.Equal("KhongCoSetupNayDau", Assert.Single(invalid));
    }

    // ── Thuế điểm cho chiều bán khống ───────────────────────────────────

    private SizingResult Size(int score, TradeDirection? direction, Action<EngineSetting>? configure = null)
    {
        var settings = ScoringFixtures.Settings(configure);
        return _sizer.Calculate(
            Score(score), ScoringFixtures.Plan(), GateAggregate.Neutral, 1m, settings,
            setup: null, direction: direction);
    }

    /// <summary>
    /// MẶC ĐỊNH thuế phải bằng 0 — hai chiều dùng chung ngưỡng.
    /// </summary>
    /// <remarks>
    /// Đây là test quan trọng nhất của nhóm này, và nó khoá một KẾT LUẬN ĐÃ BỊ LẬT. Nhìn vào
    /// lãi/lỗ lệnh thật thì Short trông như lỗ thủng (12 lệnh, −64,58 USDT); đo bằng net R trên
    /// 64 phiếu có kết cục thì Short +0,498 còn Long −0,108 — Short là chiều TỐT hơn.
    ///
    /// Bật thuế lên 5 điểm loại 27/68 phiếu và kéo net R trung bình từ +0,254 xuống +0,025.
    /// Nếu ai đó đổi mặc định này, test đỏ, và họ phải mang số liệu mới ra.
    /// </remarks>
    [Fact]
    public void Mac_dinh_khong_thu_thue_chieu_ban()
    {
        Assert.Equal(0, ScoringFixtures.Settings().ShortEntryScorePenalty);
        Assert.True(Size(57, TradeDirection.Short).FinalSizeR > 0m);
        Assert.True(Size(57, TradeDirection.Long).FinalSizeR > 0m);
    }

    /// <remarks>
    /// Cơ chế vẫn phải chạy đúng khi được bật bằng tay: ngưỡng 55 + thuế 5 = 60, nên điểm 57 đủ
    /// cho lệnh mua và thiếu cho lệnh bán.
    /// </remarks>
    [Fact]
    public void Bat_thue_bang_tay_thi_lenh_ban_phai_dat_nguong_cao_hon()
    {
        Assert.True(Size(57, TradeDirection.Long, s => s.ShortEntryScorePenalty = 5).FinalSizeR > 0m);
        Assert.Equal(0m, Size(57, TradeDirection.Short, s => s.ShortEntryScorePenalty = 5).FinalSizeR);
        Assert.True(Size(60, TradeDirection.Short, s => s.ShortEntryScorePenalty = 5).FinalSizeR > 0m);
    }

    [Fact]
    public void Ly_do_tu_choi_noi_ro_la_do_thue_chieu_ban()
    {
        Assert.Contains("phụ thu chiều bán khống",
            Size(57, TradeDirection.Short, s => s.ShortEntryScorePenalty = 5).ReasonVi);
    }

    /// <summary>Không nêu chiều thì không thu thuế — giữ nguyên hành vi của nơi gọi cũ.</summary>
    [Fact]
    public void Khong_neu_chieu_thi_khong_thu_thue()
    {
        Assert.True(Size(57, direction: null, s => s.ShortEntryScorePenalty = 5).FinalSizeR > 0m);
    }
}
