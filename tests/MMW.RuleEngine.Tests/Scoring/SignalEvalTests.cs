using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Interfaces;
using MMW.Application.Services;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Scoring;
using MMW.Domain.DbContext;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// T084 / T085 / T086 — chu kỳ đánh giá chạy được khi không có AI, không tự nới ngưỡng, và
/// lưu phiếu mọi lần.
/// </summary>
public class SignalEvalNoThresholdRelaxationTests
{
    private static readonly DateTime RunAt = new(2026, 8, 5, 14, 1, 0, DateTimeKind.Utc);
    private const string Symbol = "BTCUSDT";

    private static async Task<TimeGuardHarness> HarnessAsync(
        Action<Domain.Entities.EngineSetting>? configure = null, bool withPlan = true)
    {
        var harness = await TimeGuardHarness.CreateAsync(s =>
        {
            s.Symbols = Symbol;
            configure?.Invoke(s);
        });

        harness.Clock.UtcNow = RunAt;
        harness.MarketData.Prices[Symbol] = 1000m;
        harness.MarketData.Candles[Symbol] = ScoringFixtures.ZigZag(300, interval: TimeSpan.FromMinutes(15));
        harness.MarketData.FearGreed = 55;

        if (withPlan)
        {
            using var scope = harness.NewScope();
            var plans = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();
            await plans.GenerateAsync(harness.AccountId, DateOnly.FromDateTime(RunAt));
        }

        return harness;
    }

    private static async Task<Domain.Entities.EntryScorecard> EvaluateAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        return await service.EvaluateAsync(harness.AccountId, Symbol, RunAt);
    }

    // ── T084 / SC-001 — chạy trọn vẹn khi không có AI ───────────────────

    [Fact]
    public async Task Khong_cau_hinh_AI_thi_chu_ky_van_chay_tron_ven()
    {
        using var harness = await HarnessAsync();

        // Không đăng ký ILlmService nào trong bộ khung — nếu chu kỳ đánh giá cần nó thì
        // lời gọi dưới đây sẽ ném ngay tại đây.
        var card = await EvaluateAsync(harness);

        Assert.NotNull(card);
        Assert.NotEqual(0, card.Id);
        Assert.Equal(Symbol, card.Symbol);
    }

    [Fact]
    public async Task Khong_lop_nao_cua_chu_ky_danh_gia_nhan_dich_vu_AI()
    {
        // FR-041 cưỡng chế bằng KIẾN TRÚC: nếu tầng quyết định không với tới được dịch vụ AI
        // thì nó không thể bị AI chi phối, bất kể ai viết mã sau này nghĩ gì.
        var offenders = typeof(SignalEvalService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => typeof(ILlmService).IsAssignableFrom(p.ParameterType))
            .Select(p => p.Name!)
            .ToList();

        Assert.Empty(offenders);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Phieu_co_du_13_dong_tieu_chi_va_9_dong_ky_luat()
    {
        using var harness = await HarnessAsync();

        var card = await EvaluateAsync(harness);

        // 13 tiêu chí chấm điểm + 9 rào kỷ luật. Ghi cả rào đang cho qua chứ không chỉ rào
        // đang chặn: phiếu phải trả lời được "những rào nào đã được kiểm và đều ổn".
        //
        // Con số 9 gồm 8 rào chạy qua `_gates` — trong đó hai rào của V2 là
        // `discipline.open_position` (chặn vào lại cùng một ý tưởng trên mã đang có vị thế) và
        // `discipline.correlated_exposure` (cộng dồn rủi ro trên các mã đi cùng pha) — cộng thêm
        // `discipline.time_guard`, cổng chặn giờ chạy ngoài `_gates` nên được ghi tay.
        //
        // Ghim số lượng ở đây có chủ ý — thêm rào mà quên đăng ký DI thì test này đỏ, còn nếu chỉ
        // ghim tên từng rào thì một rào bị bỏ đăng ký sẽ lọt qua im lặng.
        Assert.Equal(13, card.Lines.Count(l => l.Group != ScoreGroup.Discipline));
        Assert.Equal(9, card.Lines.Count(l => l.Group == ScoreGroup.Discipline));
        Assert.All(card.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Reason)));
    }

    [Fact]
    public async Task Dong_ky_luat_khong_bao_gio_cong_diem()
    {
        using var harness = await HarnessAsync();

        var card = await EvaluateAsync(harness);

        Assert.All(card.Lines.Where(l => l.Group == ScoreGroup.Discipline),
            l => Assert.True(l.AwardedPoints <= 0, $"{l.CriterionKey} cộng {l.AwardedPoints} điểm."));
    }

    // ── T085 / FR-038 — không tự nới ngưỡng ─────────────────────────────

    [Fact]
    public async Task Ca_ngay_khong_setup_nao_dat_nguong_thi_ra_0_lenh()
    {
        // Đặt ngưỡng vào lệnh lên 100 — cao hơn tổng điểm tối đa 80 mà 13 tiêu chí có thể cho.
        // Zero lệnh là kết quả ĐÚNG, không phải lỗi.
        using var harness = await HarnessAsync(s => s.MinScoreToEnter = 100);

        var card = await EvaluateAsync(harness);

        Assert.NotEqual(ScorecardOutcome.Entered, card.Outcome);
        Assert.Equal(0m, card.FinalSizeR);
    }

    [Fact]
    public async Task Chay_lien_tuc_nhieu_chu_ky_khong_lam_nguong_tu_ha()
    {
        // Kịch bản mà FR-038 chống: hệ thống "học" rằng nó đang ra quá ít lệnh rồi tự nới.
        // Chạy nhiều lần và khẳng định ngưỡng trong cấu hình không hề dịch chuyển.
        using var harness = await HarnessAsync(s => s.MinScoreToEnter = 100);

        for (var i = 0; i < 5; i++)
        {
            harness.Clock.UtcNow = RunAt.AddMinutes(15 * i);
            using var scope = harness.NewScope();
            var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
            await service.EvaluateAsync(harness.AccountId, Symbol, harness.Clock.UtcNow);
        }

        using var verify = harness.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        var setting = await db.EngineSettings.AsNoTracking().SingleAsync();

        Assert.Equal(100, setting.MinScoreToEnter);
        Assert.False(await db.EntryScorecards.AnyAsync(c => c.Outcome == ScorecardOutcome.Entered));
    }

    [Fact]
    public async Task Khong_ton_tai_thanh_vien_nao_nghe_giong_tu_noi_nguong()
    {
        var suspicious = new[] { "Relax", "Loosen", "Lower", "AutoAdjust", "Adapt" };

        var offenders = typeof(SignalEvalService).Assembly.GetTypes()
            .Where(t => t.Namespace is not null
                        && (t.Namespace.StartsWith("MMW.Application.Trading", StringComparison.Ordinal)
                            || t == typeof(SignalEvalService)))
            .SelectMany(t => t.GetMembers().Select(m => (Type: t, Member: m)))
            .Where(x => suspicious.Any(s => x.Member.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Member.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "FR-038 cấm mọi cơ chế tự nới ngưỡng để đạt số lệnh mục tiêu. Thành viên đáng ngờ: "
            + string.Join(", ", offenders));

        await Task.CompletedTask;
    }

    // ── T086 / FR-051, SC-012 — lưu phiếu và chống trùng ────────────────

    [Fact]
    public async Task Phieu_duoc_luu_KE_CA_khi_khong_vao_lenh()
    {
        using var harness = await HarnessAsync(s => s.MinScoreToEnter = 100);

        await EvaluateAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        var card = await db.EntryScorecards.Include(c => c.Lines).SingleAsync();
        Assert.NotEqual(ScorecardOutcome.Entered, card.Outcome);
        Assert.NotEmpty(card.Lines);
    }

    [Fact]
    public async Task Job_chay_chong_lan_tren_cung_cay_nen_khong_sinh_phieu_trung()
    {
        using var harness = await HarnessAsync();

        var first = await EvaluateAsync(harness);
        var second = await EvaluateAsync(harness);

        Assert.Equal(first.Id, second.Id);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(1, await db.EntryScorecards.CountAsync());
    }

    [Fact]
    public async Task Chua_co_ke_hoach_ngay_thi_van_luu_phieu_kem_ly_do()
    {
        // Bị chặn cũng phải để lại dấu vết. Không lưu thì câu hỏi "sao hôm nay không có lệnh
        // nào" không có chỗ nào trả lời.
        using var harness = await HarnessAsync(withPlan: false);

        var card = await EvaluateAsync(harness);

        Assert.Equal(ScorecardOutcome.Vetoed, card.Outcome);
        Assert.Equal(VetoReason.NoDailyPlan, card.VetoReason);
        Assert.NotEqual(0, card.Id);
    }

    [Fact]
    public async Task Dang_trong_cua_so_chan_thi_ghi_dung_ly_do()
    {
        using var harness = await HarnessAsync();

        // 16:00 UTC là mốc thanh toán phí vốn, cửa sổ [15:55, 16:05).
        var inBlackout = new DateTime(2026, 8, 5, 16, 0, 0, DateTimeKind.Utc);
        harness.Clock.UtcNow = inBlackout;

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var card = await service.EvaluateAsync(harness.AccountId, Symbol, inBlackout);

        Assert.Equal(ScorecardOutcome.Vetoed, card.Outcome);
        Assert.Equal(VetoReason.InBlackoutWindow, card.VetoReason);
    }

    [Fact]
    public async Task Phieu_bi_chan_gio_VAN_duoc_cham_diem_day_du()
    {
        // Chặn giờ không được phép làm phiếu rỗng. Phiếu rỗng nghĩa là không có ba mức giá, mà
        // không có ba mức giá thì ScorecardOutcomeReview không mô phỏng được — và cổng blackout
        // trở thành cổng duy nhất miễn nhiễm với câu hỏi "chặn đúng hay chặn nhầm".
        using var harness = await HarnessAsync();

        var inBlackout = new DateTime(2026, 8, 5, 16, 0, 0, DateTimeKind.Utc);
        harness.Clock.UtcNow = inBlackout;

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var card = await service.EvaluateAsync(harness.AccountId, Symbol, inBlackout);

        // Đủ 14 tiêu chí, không phải "có vài dòng cho có".
        Assert.Equal(13, card.Lines.Count(l => l.Group != ScoreGroup.Discipline));

        // Ba mức giá — chính xác là thứ ScorecardOutcomeReview cần để mô phỏng.
        Assert.NotNull(card.Direction);
        Assert.NotNull(card.SuggestedEntry);
        Assert.NotNull(card.SuggestedStopLoss);
        Assert.NotNull(card.SuggestedFirstTakeProfit ?? card.SuggestedTakeProfit);
    }

    [Fact]
    public async Task Chan_gio_ghi_de_moi_ly_do_khac_va_dua_kich_thuoc_ve_0()
    {
        // Nới lỏng ở đây là nới lỏng một cổng an toàn. Bài kiểm tra này tồn tại để lần sửa sau
        // không biến "chấm trước rồi chặn" thành "chấm trước rồi quên chặn".
        using var harness = await HarnessAsync();

        var inBlackout = new DateTime(2026, 8, 5, 16, 0, 0, DateTimeKind.Utc);
        harness.Clock.UtcNow = inBlackout;

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var card = await service.EvaluateAsync(harness.AccountId, Symbol, inBlackout);

        Assert.Equal(ScorecardOutcome.Vetoed, card.Outcome);
        Assert.Equal(VetoReason.InBlackoutWindow, card.VetoReason);
        Assert.Equal(0m, card.FinalSizeR);

        // Cổng chặn giờ cũng phải để lại một dòng, và dòng đó phải được đánh dấu là veto cứng.
        var line = Assert.Single(card.Lines, l => l.CriterionKey == "discipline.time_guard");
        Assert.True(line.IsHardVeto);
    }

    [Fact]
    public async Task Ngoai_cua_so_chan_van_ghi_mot_dong_cho_cong_gio()
    {
        // Phiếu phải phân biệt được "đã kiểm giờ và ngoài mọi cửa sổ" với "chưa bao giờ kiểm giờ".
        using var harness = await HarnessAsync();

        var card = await EvaluateAsync(harness);

        var line = Assert.Single(card.Lines, l => l.CriterionKey == "discipline.time_guard");
        Assert.False(line.IsHardVeto);
    }

    [Fact]
    public async Task Cham_diem_moi_ma_trong_danh_sach_theo_doi()
    {
        using var harness = await HarnessAsync(s => s.Symbols = "BTCUSDT,ETHUSDT");
        harness.MarketData.Prices["ETHUSDT"] = 3000m;
        harness.MarketData.Candles["ETHUSDT"] = ScoringFixtures.ZigZag(300, start: 3000m, interval: TimeSpan.FromMinutes(15));

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var cards = await service.EvaluateAllAsync(harness.AccountId, RunAt);

        Assert.Equal(2, cards.Count);
        Assert.Contains(cards, c => c.Symbol == "ETHUSDT");
    }

    [Fact]
    public async Task Nguon_du_lieu_chet_khong_lam_chet_chu_ky_danh_gia()
    {
        using var harness = await HarnessAsync();
        harness.MarketData.ThrowOnCandles = true;

        var card = await EvaluateAsync(harness);

        // Phiếu vẫn ra đời, chỉ là điểm thấp vì hàng loạt tiêu chí thiếu dữ liệu (FR-006).
        Assert.NotNull(card);
        Assert.Contains(card.Lines, l => !l.DataAvailable);
        Assert.All(card.Lines.Where(l => !l.DataAvailable), l => Assert.Equal(0, l.AwardedPoints));
    }

    [Fact]
    public async Task Backtest_co_the_truyen_thong_ke_mo_phong_de_gate_gioi_han_lenh()
    {
        using var harness = await HarnessAsync();
        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var statistics = TraderStatistics.Empty with { TradesToday = 999 };

        var card = await service.EvaluateWithStatisticsAsync(
            harness.AccountId, Symbol, RunAt, statistics, persistResult: false);

        Assert.Equal(ScorecardOutcome.Vetoed, card.Outcome);
        Assert.Contains(card.Lines, l =>
            l.CriterionKey == "discipline.max_trades" && l.IsHardVeto);
    }

    [Fact]
    public async Task Gate_oversize_dung_rui_ro_sau_sizing_so_bo_khong_dung_tran_tai_khoan()
    {
        using var harness = await HarnessAsync();
        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var risk = await db.RiskSettings.SingleAsync();
        risk.MaxRiskPerTradePercent = 20m;
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        var statistics = TraderStatistics.Empty with { AverageRiskRecent = 5m };
        var card = await service.EvaluateWithStatisticsAsync(
            harness.AccountId, Symbol, RunAt, statistics, persistResult: false);

        var oversize = Assert.Single(card.Lines, l => l.CriterionKey == "discipline.oversized");
        Assert.False(oversize.IsHardVeto);
        Assert.Contains("trong trần", oversize.Reason);
    }
}
