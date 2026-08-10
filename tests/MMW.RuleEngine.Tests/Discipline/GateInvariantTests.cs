using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Discipline.Gates;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Discipline;

internal static class DisciplineFixtures
{
    /// <summary>Đúng bộ tám rào chắn mà DI đăng ký.</summary>
    public static IReadOnlyList<IDisciplineGate> AllGates() => new IDisciplineGate[]
    {
        new LossStreakGate(),
        new DailyLossLimitGate(),
        new RevengeWindowGate(),
        new OversizedGate(),
        new MaxTradesGate(),
        new WorstHoursGate(),
        new OpenPositionGate(),
        new CorrelatedExposureGate(),
    };
}

/// <summary>
/// T113 — bất biến của cả tầng: KHÔNG rào nào được trả <c>SizeMultiplier &gt; 1.0</c>.
/// </summary>
/// <remarks>
/// Cưỡng chế bằng cách quét toàn bộ không gian trạng thái chứ không bằng vài ca mẫu. Một rào
/// làm lệnh TO LÊN là đúng thứ mà cả tầng này tồn tại để ngăn, và nó sẽ không hiện ra ở bất kỳ
/// test chức năng nào — con số vẫn hợp lệ, chỉ là sai chiều.
/// </remarks>
public class GateInvariantTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);

    private static DisciplineContext Context(TraderStatistics stats, decimal plannedRisk, int maxTrades) => new()
    {
        TradingAccountId = 1,
        EvaluatedAtUtc = Now,
        Symbol = ScoringFixtures.Symbol,
        Direction = TradeDirection.Long,
        PlannedRiskPercent = plannedRisk,
        DailyPlan = ScoringFixtures.Plan(maxTrades: maxTrades),
        Settings = EngineSettingDefaults.Create(1),
        RiskSettings = new RiskSetting { TradingAccountId = 1 },
        Stats = stats,
    };

    private static IEnumerable<DisciplineContext> AllStates()
    {
        foreach (var losses in new[] { 0, 1, 2, 3, 10 })
        foreach (var dailyLoss in new[] { 0m, 1m, 3m, 20m })
        foreach (var lastLoss in new DateTime?[] { null, Now.AddMinutes(-1), Now.AddMinutes(-60) })
        foreach (var avgRisk in new decimal?[] { null, 0.5m, 1m })
        foreach (var today in new[] { 0, 4, 5, 99 })
        foreach (var closed in new[] { 0, 49, 50, 500 })
        {
            yield return Context(
                new TraderStatistics(losses, dailyLoss, lastLoss, avgRisk, today, closed, new[] { 14, 3 }),
                plannedRisk: 1.6m,
                maxTrades: 5);
        }
    }

    [Fact]
    public void Khong_rao_nao_tra_he_so_lon_hon_1()
    {
        var gates = DisciplineFixtures.AllGates();
        var offenders = new List<string>();

        foreach (var context in AllStates())
        foreach (var gate in gates)
        {
            var result = gate.Evaluate(context);

            if (result.SizeMultiplier > 1.0m)
                offenders.Add($"{gate.Key} trả hệ số {result.SizeMultiplier}");
        }

        Assert.True(offenders.Count == 0,
            "Không rào kỷ luật nào được làm lệnh to lên: " + string.Join(", ", offenders.Distinct()));
    }

    [Fact]
    public void Khong_rao_nao_tra_he_so_am()
    {
        foreach (var context in AllStates())
        foreach (var gate in DisciplineFixtures.AllGates())
        {
            Assert.True(gate.Evaluate(context).SizeMultiplier >= 0m, $"{gate.Key} trả hệ số âm.");
        }
    }

    [Fact]
    public void Khong_rao_nao_CONG_diem()
    {
        // Nhóm kỷ luật chỉ trừ. Một điểm thưởng lọt vào đây sẽ làm tổng vượt 85 và phá vỡ
        // thiết kế "điểm 100 tuyệt đối là không đạt được".
        foreach (var context in AllStates())
        foreach (var gate in DisciplineFixtures.AllGates())
        {
            Assert.True(gate.Evaluate(context).ScorePenalty <= 0, $"{gate.Key} cộng điểm.");
        }
    }

    [Fact]
    public void Khoa_cua_sau_rao_deu_duy_nhat()
    {
        var keys = DisciplineFixtures.AllGates().Select(g => g.Key).ToList();

        Assert.Equal(8, keys.Count);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Khong_rao_nao_nem_tren_bat_ky_trang_thai_nao()
    {
        // Một ngoại lệ ở đây làm chết cả chu kỳ đánh giá của symbol đó, âm thầm.
        foreach (var context in AllStates())
        foreach (var gate in DisciplineFixtures.AllGates())
        {
            var result = gate.Evaluate(context);
            Assert.NotNull(result);
        }
    }

    // ── Bộ gộp ──────────────────────────────────────────────────────────

    [Fact]
    public void Bo_gop_nhan_don_cac_he_so_chu_khong_lay_cai_nho_nhat()
    {
        // Hai rào cùng yêu cầu giảm một nửa thì thành một phần tư. Lấy giá trị nhỏ nhất sẽ
        // bỏ qua một trong hai cảnh báo.
        var runner = new DisciplineGateRunner(new IDisciplineGate[] { new HalfGate("a"), new HalfGate("b") });

        var outcome = runner.Run(Context(TraderStatistics.Empty, 1m, 5));

        Assert.Equal(0.25m, outcome.Aggregate.SizeMultiplier);
    }

    [Fact]
    public void Bo_gop_chay_HET_cac_rao_chu_khong_dung_som()
    {
        // Khác vòng chấm điểm: trader cần thấy TOÀN BỘ những gì đang chặn mình, không chỉ
        // cái đầu tiên.
        var runner = new DisciplineGateRunner(DisciplineFixtures.AllGates());

        var outcome = runner.Run(Context(
            new TraderStatistics(10, 20m, Now.AddMinutes(-1), 1m, 99, 500, new[] { 14 }), 1.6m, 5));

        Assert.Equal(8, outcome.Lines.Count);
    }

    [Fact]
    public void Dung_ngay_thang_moi_thu()
    {
        var runner = new DisciplineGateRunner(DisciplineFixtures.AllGates());

        var outcome = runner.Run(Context(
            new TraderStatistics(10, 20m, Now.AddMinutes(-1), 1m, 99, 500, new[] { 14 }), 1.6m, 5));

        Assert.True(outcome.Aggregate.IsBlocked);
        Assert.Equal(0m, outcome.Aggregate.SizeMultiplier);
        Assert.Contains(outcome.Aggregate.VetoReason,
            new VetoReason?[] { VetoReason.LossStreakStop, VetoReason.DailyLossStop });
    }

    [Fact]
    public void Khong_rao_nao_kich_hoat_thi_he_so_bang_1()
    {
        var runner = new DisciplineGateRunner(DisciplineFixtures.AllGates());

        var outcome = runner.Run(Context(TraderStatistics.Empty, 1m, 5));

        Assert.False(outcome.Aggregate.IsBlocked);
        Assert.Equal(1.0m, outcome.Aggregate.SizeMultiplier);
        Assert.Equal(0, outcome.Aggregate.ScorePenalty);
    }

    [Fact]
    public void Bo_gop_kep_he_so_hon_1_do_rao_tra_nham()
    {
        // Bộ gộp KHÔNG tin rào. Một hệ số > 1 lọt qua sẽ làm lệnh to lên — đúng điều cả tầng
        // này tồn tại để ngăn.
        var runner = new DisciplineGateRunner(new IDisciplineGate[] { new RogueGate() });

        Assert.Equal(1.0m, runner.Run(Context(TraderStatistics.Empty, 1m, 5)).Aggregate.SizeMultiplier);
    }

    private sealed class HalfGate : IDisciplineGate
    {
        public HalfGate(string key) => Key = $"discipline.{key}";
        public string Key { get; }
        public GateResult Evaluate(DisciplineContext context) => GateResult.Reduce(0.5m, "giảm một nửa");
    }

    /// <summary>Rào cố tình vi phạm, để chứng minh bộ gộp thực sự kẹp chứ không chỉ tin tưởng.</summary>
    private sealed class RogueGate : IDisciplineGate
    {
        public string Key => "discipline.rogue";
        public GateResult Evaluate(DisciplineContext context) =>
            new(GateAction.ReduceSize, 5.0m, 0, "cố tình trả hệ số phóng to", null);
    }
}
