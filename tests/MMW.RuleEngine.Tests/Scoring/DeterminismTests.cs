using System.Reflection;
using MMW.Application.Interfaces;
using MMW.Application.Trading.Scoring;
using MMW.RuleEngine.Tests.Constitution;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// T082 / SC-002 — chấm cùng một bối cảnh 100 lần cho ra 100 kết quả giống hệt.
/// </summary>
/// <remarks>
/// Một trăm lần chứ không phải hai: nguồn bất định hay gặp nhất trong .NET không phải đồng hồ
/// mà là THỨ TỰ DUYỆT của tập hợp băm. Nó ổn định trong một tiến trình nhưng có thể đổi giữa
/// các lần chạy, và một phép cộng số thực theo thứ tự khác nhau sẽ cho con số khác nhau ở chữ
/// số cuối. Lặp nhiều lần trong cùng tiến trình không bắt được điều đó, nên các test cấu trúc
/// bên dưới mới là phần gác chính; con số 100 chỉ bắt được trạng thái ẩn tích luỹ.
/// </remarks>
public class DeterminismTests
{
    [Fact]
    public void Cham_mot_tram_lan_cho_ra_mot_tram_ket_qua_giong_het()
    {
        var scorer = new EntryScorer(ScoringFixtures.AllCriteria());
        var context = ScoringFixtures.Context();

        var first = scorer.Score(context);

        for (var i = 0; i < 100; i++)
        {
            var again = scorer.Score(context);

            Assert.Equal(first.TotalScore, again.TotalScore);
            Assert.Equal(first.TechnicalScore, again.TechnicalScore);
            Assert.Equal(first.MarketScore, again.MarketScore);
            Assert.Equal(first.LiquidityScore, again.LiquidityScore);
            Assert.Equal(first.DisciplinePenalty, again.DisciplinePenalty);
            Assert.Equal(first.IsVetoed, again.IsVetoed);
            Assert.Equal(
                first.Lines.Select(l => (l.Key, l.Result.AwardedPoints, l.Result.Reason)),
                again.Lines.Select(l => (l.Key, l.Result.AwardedPoints, l.Result.Reason)));
        }
    }

    [Fact]
    public void Moi_lop_scorer_moi_van_cho_cung_ket_qua()
    {
        // Dựng lại scorer từ đầu để loại khả năng kết quả ổn định chỉ vì một bộ nhớ đệm
        // nội bộ nào đó đang giữ lần tính đầu tiên.
        var context = ScoringFixtures.Context();

        var a = new EntryScorer(ScoringFixtures.AllCriteria()).Score(context);
        var b = new EntryScorer(ScoringFixtures.AllCriteria()).Score(context);

        Assert.Equal(a.TotalScore, b.TotalScore);
        Assert.Equal(a.Lines.Select(l => l.Key), b.Lines.Select(l => l.Key));
    }

    // ── Gác bằng cấu trúc, không chỉ bằng phép lặp ──────────────────────

    [Fact]
    public void Khong_tieu_chi_nao_cham_dong_ho_he_thong_hay_so_ngau_nhien()
    {
        // Ràng buộc 4 của contract. Bộ quét IL nhìn vào THÂN phương thức, thứ mà reflection
        // thường không thấy — một DateTime.UtcNow lọt vào đây sẽ không hiện ra ở chữ ký nào.
        var calls = IlScanner.ScanCalls(
            typeof(EntryScorer).Assembly,
            ns => ns == "MMW.Application.Trading.Scoring"
                  || ns == "MMW.Application.Trading.Scoring.Criteria");

        var forbidden = new[]
        {
            ("System.DateTime", "get_UtcNow"),
            ("System.DateTime", "get_Now"),
            ("System.DateTime", "get_Today"),
            ("System.Random", ".ctor"),
            ("System.Guid", "NewGuid"),
        };

        var violations = calls
            .Where(c => forbidden.Any(f => f.Item1 == c.TargetType && f.Item2 == c.TargetMember))
            .Select(c => c.ToString())
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0,
            "Tiêu chí phải lấy thời gian từ context.EvaluatedAtUtc:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Khong_tieu_chi_nao_nhan_dich_vu_AI_trong_constructor()
    {
        // Ràng buộc 5 của contract, FR-041.
        var offenders = typeof(EntryScorer).Assembly.GetTypes()
            .Where(t => typeof(IScoreCriterion).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => (Type: t, Param: p))))
            .Where(x => typeof(ILlmService).IsAssignableFrom(x.Param.ParameterType))
            .Select(x => $"{x.Type.Name}({x.Param.ParameterType.Name})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Tiêu chí chấm điểm không được phụ thuộc dịch vụ AI (FR-041): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Bo_gac_thuc_su_quet_duoc_lop_tieu_chi()
    {
        // Một bộ gác xanh vì không quét gì còn tệ hơn không có bộ gác.
        var scanned = IlScanner.CountTypes(
            typeof(EntryScorer).Assembly,
            ns => ns == "MMW.Application.Trading.Scoring.Criteria");

        Assert.True(scanned > 0, "Bộ lọc namespace đã lệch khỏi cấu trúc mã.");
    }
}
