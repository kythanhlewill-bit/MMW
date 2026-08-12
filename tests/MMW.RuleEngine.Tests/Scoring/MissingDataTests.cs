using MMW.Application.Trading.Scoring;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// T080 / FR-006 — mọi tiêu chí, khi thiếu dữ liệu, phải trả ĐÚNG 0 điểm.
/// </summary>
/// <remarks>
/// Không phải điểm trung bình, không phải điểm tối đa, không phải "bỏ qua tiêu chí rồi chia
/// lại thang". Cả ba cách thay thế đều có vẻ hợp lý và cả ba đều biến một nguồn dữ liệu chết
/// thành điểm thưởng — nghĩa là hệ thống sẽ vào lệnh TỰ TIN HƠN đúng vào lúc nó biết ít hơn.
///
/// Quét toàn bộ 13 tiêu chí bằng một vòng lặp thay vì viết 13 test: thêm tiêu chí mới mà quên
/// xử lý thiếu dữ liệu thì test này đỏ ngay, không cần ai nhớ thêm test.
/// </remarks>
public class MissingDataTests
{
    [Fact]
    public void Moi_tieu_chi_thieu_du_lieu_deu_tra_0_diem()
    {
        var starved = ScoringFixtures.Starved();
        var offenders = new List<string>();

        foreach (var criterion in ScoringFixtures.AllCriteria())
        {
            var result = criterion.Evaluate(starved);

            if (!result.DataAvailable && result.AwardedPoints != 0)
                offenders.Add($"{criterion.Key} trả {result.AwardedPoints} điểm dù DataAvailable = false");
        }

        Assert.True(offenders.Count == 0,
            "FR-006: thiếu dữ liệu ⟹ 0 điểm. Vi phạm:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Moi_tieu_chi_deu_chiu_duoc_boi_canh_trong_rong_ma_khong_nem()
    {
        // Nguồn dữ liệu chết không được làm chết cả chu kỳ đánh giá. Một ngoại lệ ở đây
        // nghĩa là cả symbol đó im lặng biến mất khỏi vòng chấm điểm.
        var starved = ScoringFixtures.Starved();

        foreach (var criterion in ScoringFixtures.AllCriteria())
        {
            var result = criterion.Evaluate(starved);

            Assert.False(string.IsNullOrWhiteSpace(result.Reason),
                $"{criterion.Key} không nêu lý do khi thiếu dữ liệu.");
        }
    }

    [Fact]
    public void Boi_canh_trong_rong_chi_con_diem_cua_tieu_chi_khong_can_nguon_ngoai()
    {
        // Tắt luật phủ dữ liệu để đo đúng thứ test này quan tâm: SỐ HỌC của vòng tổng hợp.
        var starved = ScoringFixtures.Starved() with
        {
            Settings = ScoringFixtures.Settings(s => s.MinDataCoveragePercent = 0m),
        };

        var outcome = new EntryScorer(ScoringFixtures.AllCriteria()).Score(starved);

        // Đúng MỘT tiêu chí còn chấm được: `market.day_regime_match` đọc thẳng từ kế hoạch
        // ngày, thứ luôn có mặt và không phụ thuộc nguồn nào ở ngoài. Mười hai tiêu chí còn
        // lại phải về 0.
        Assert.False(outcome.IsVetoed);

        var scoring = outcome.Lines.Where(l => l.Result.AwardedPoints != 0).ToList();
        Assert.Single(scoring);
        Assert.Equal("market.day_regime_match", scoring[0].Key);
        Assert.Equal(scoring[0].Result.AwardedPoints, outcome.TotalScore);
    }

    /// <summary>
    /// Mất gần hết nguồn dữ liệu thì KHÔNG vào lệnh, dù phần còn lại chấm đẹp đến mấy.
    /// </summary>
    /// <remarks>
    /// Đây là chốt chặn đi kèm việc chuẩn hoá ngưỡng theo điểm đo được. Chuẩn hoá là đúng — nó
    /// xoá bỏ chênh lệch giữa kiểm thử lịch sử (trần 75) và chạy thật (trần 85) — nhưng nếu để
    /// nó chạy không giới hạn thì một tài khoản chỉ còn hai tiêu chí sống vẫn vào lệnh đều đặn,
    /// và tỉ lệ phần trăm sẽ trông rất đẹp đúng vì mẫu số đã teo lại.
    ///
    /// Hình phạt thiếu dữ liệu của FR-006 vì vậy chuyển vai: từ "khó vào lệnh hơn một cách không
    /// kiểm soát" sang "vào nhỏ hơn một cách tường minh", cộng một ngưỡng cứng ở đây.
    /// </remarks>
    [Fact]
    public void Mat_gan_het_nguon_du_lieu_thi_veto_chu_khong_chuan_hoa_de_vao_lenh()
    {
        var outcome = new EntryScorer(ScoringFixtures.AllCriteria()).Score(ScoringFixtures.Starved());

        Assert.True(outcome.IsVetoed);
        Assert.Equal(VetoReason.InsufficientData, outcome.VetoReason);
        Assert.True(outcome.AvailableMaxPoints < outcome.TotalMaxPoints);
    }

    /// <summary>
    /// Mười điểm mà kiểm thử lịch sử không dựng lại được KHÔNG được làm ngưỡng vào lệnh gắt lên.
    /// </summary>
    /// <remarks>
    /// <c>liquidity.open_interest</c> và <c>liquidity.spread_depth</c> luôn bằng 0 khi chạy lịch
    /// sử, nên trần thực tế là 70 chứ không phải 80. So ngưỡng tuyệt đối làm kiểm thử đòi 78,6%
    /// còn chạy thật chỉ đòi 68,8% — kiểm thử lọc gắt hơn gần 10 điểm phần trăm, và lệch đó chỉ
    /// có một chiều: làm báo cáo đẹp hơn thực tế.
    /// </remarks>
    [Fact]
    public void Mat_10_diem_thanh_khoan_khong_lam_nguong_vao_lenh_gat_len()
    {
        var context = ScoringFixtures.Context();
        var blindToLiquidity = context with { OpenInterest = null, Depth = null };

        var full = new EntryScorer(ScoringFixtures.AllCriteria()).Score(context);
        var partial = new EntryScorer(ScoringFixtures.AllCriteria()).Score(blindToLiquidity);

        // Thang tổng luôn là 80 bất kể đo được bao nhiêu.
        Assert.Equal(80, full.TotalMaxPoints);
        Assert.Equal(80, partial.TotalMaxPoints);

        // Tắt đúng hai nguồn ⟹ mất đúng 10 điểm thang đo, không hơn không kém. Sau khi gỡ
        // `liquidity.zone_position`, hai nguồn này là TOÀN BỘ nhóm thanh khoản.
        Assert.Equal(10, full.AvailableMaxPoints - partial.AvailableMaxPoints);

        // Và đây là điều quan trọng: ngưỡng 55 trên thang 80 tương đương 48,1 trên thang 70.
        // Một phiếu 49 điểm ở chế độ kiểm thử phải qua được, y như phiếu 55 điểm ở chạy thật.
        var backtestScale = new Func<int, ScoringOutcome>(score =>
            new ScoringOutcome(score, 0, 0, 0, 0, false, null, null, Array.Empty<ScoredLine>(), 80, 70));

        Assert.False(backtestScale(48).Reaches(55));
        Assert.True(backtestScale(49).Reaches(55));

        // So tuyệt đối như trước đây thì phiếu 49 điểm bị loại ở kiểm thử nhưng phiếu 55 điểm
        // lại được nhận ở chạy thật — cùng một chất lượng setup, hai kết luận khác nhau.
        Assert.True(new ScoringOutcome(55, 0, 0, 0, 0, false, null, null, Array.Empty<ScoredLine>(), 80, 80).Reaches(55));
    }

    [Fact]
    public void Diem_tra_ve_luon_nam_trong_mien_cho_phep_cua_nhom()
    {
        // Ràng buộc 1 và 2 của contract, quét trên cả bối cảnh đầy đủ lẫn bối cảnh rỗng.
        foreach (var context in new[] { ScoringFixtures.Context(), ScoringFixtures.Starved() })
        foreach (var criterion in ScoringFixtures.AllCriteria())
        {
            var result = criterion.Evaluate(context);

            if (criterion.Group == ScoreGroup.Discipline)
            {
                Assert.True(result.AwardedPoints <= 0, $"{criterion.Key} cộng điểm ở nhóm chỉ-trừ.");
                continue;
            }

            Assert.InRange(result.AwardedPoints, 0, criterion.MaxPoints);
        }
    }

    [Fact]
    public void Thieu_du_lieu_khong_bao_gio_sinh_veto_cung()
    {
        // Thiếu dữ liệu phải làm điểm thấp đi, không được biến thành lệnh cấm. Cấm vì thiếu
        // dữ liệu sẽ khiến một lần sàn chậm mạng làm đứng cả ngày giao dịch.
        var starved = ScoringFixtures.Starved();

        foreach (var criterion in ScoringFixtures.AllCriteria())
        {
            var result = criterion.Evaluate(starved);

            if (result.IsHardVeto)
            {
                Assert.True(result.DataAvailable,
                    $"{criterion.Key} veto cứng trong khi tự khai là thiếu dữ liệu — hai trạng thái này loại trừ nhau.");
            }
        }
    }
}
