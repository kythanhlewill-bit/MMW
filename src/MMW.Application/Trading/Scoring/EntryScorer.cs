using MMW.Domain.Enums;

namespace MMW.Application.Trading.Scoring;

public interface IEntryScorer
{
    ScoringOutcome Score(ScoringContext context);
}

/// <summary>
/// Vòng tổng hợp điểm. KHÔNG biết tiêu chí cụ thể nào tồn tại (Nguyên tắc V).
/// </summary>
/// <remarks>
/// Nhận <c>IEnumerable&lt;IScoreCriterion&gt;</c> từ DI. Thêm tiêu chí = thêm một lớp và một
/// dòng đăng ký. <b>Sửa lớp này để thêm tiêu chí là vi phạm Nguyên tắc V</b> — và cách chắc
/// nhất để biết mình đang vi phạm là thấy tên một tiêu chí xuất hiện trong tệp này.
/// </remarks>
public sealed class EntryScorer : IEntryScorer
{
    private readonly IReadOnlyList<IScoreCriterion> _criteria;
    private readonly int _totalMaxPoints;
    private readonly int _directionalMaxPoints;

    public EntryScorer(IEnumerable<IScoreCriterion> criteria)
    {
        // Sắp xếp MỘT LẦN theo (Group, Key) để thứ tự duyệt tất định bất kể DI trả về theo
        // thứ tự nào. Thứ tự quan trọng vì gặp veto cứng là dừng, nên nó quyết định lý do nào
        // được ghi vào phiếu.
        _criteria = criteria
            .OrderBy(c => c.Group)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        // Thang điểm suy ra từ chính bộ tiêu chí, không viết cứng 85. Nhóm kỷ luật chỉ trừ nên
        // không góp vào trần.
        _totalMaxPoints = _criteria
            .Where(c => c.Group != ScoreGroup.Discipline)
            .Sum(c => c.MaxPoints);

        // Thang điểm của riêng phần ĐỔI THEO CHIỀU. Suy ra từ chính bộ tiêu chí, cùng lý do như
        // trên: một con số viết cứng sẽ tự sai đi mỗi lần thêm/bớt tiêu chí, và sai ở đây nghĩa
        // là chênh lệch chẩn đoán bỗng mang một ý nghĩa khác mà không ai đổi nó.
        _directionalMaxPoints = _criteria
            .Where(c => c.Group != ScoreGroup.Discipline && c.IsDirectional)
            .Sum(c => c.MaxPoints);
    }

    public ScoringOutcome Score(ScoringContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lines = new List<ScoredLine>(_criteria.Count);
        int technical = 0, market = 0, liquidity = 0, discipline = 0;
        var availableMax = 0;
        var directional = 0;

        foreach (var criterion in _criteria)
        {
            var result = criterion.Evaluate(context);
            var line = new ScoredLine(criterion.Key, criterion.Group, criterion.MaxPoints, result);
            lines.Add(line);

            if (result.IsHardVeto)
            {
                // Dừng NGAY. Đây là hành vi bắt buộc chứ không phải tối ưu hoá: nó giữ cho
                // phiếu nêu đúng MỘT lý do từ chối thay vì một danh sách gây nhiễu. Chạy tiếp
                // rồi gom nhiều lý do sẽ khiến câu hỏi "vì sao lệnh này bị loại" có bốn câu
                // trả lời, và không câu nào là câu quyết định.
                return new ScoringOutcome(
                    TotalScore: 0,
                    TechnicalScore: technical,
                    MarketScore: market,
                    LiquidityScore: liquidity,
                    DisciplinePenalty: discipline,
                    IsVetoed: true,
                    VetoReason: result.VetoReason,
                    VetoDetail: result.Reason,
                    Lines: lines,
                    TotalMaxPoints: _totalMaxPoints,
                    AvailableMaxPoints: availableMax,
                    DirectionalScore: directional,
                    DirectionalMaxPoints: _directionalMaxPoints);
            }

            if (criterion.Group != ScoreGroup.Discipline && result.DataAvailable)
                availableMax += criterion.MaxPoints;

            var points = Normalise(criterion, result);

            if (criterion.Group != ScoreGroup.Discipline && criterion.IsDirectional)
                directional += points;

            switch (criterion.Group)
            {
                case ScoreGroup.Technical: technical += points; break;
                case ScoreGroup.Market: market += points; break;
                case ScoreGroup.Liquidity: liquidity += points; break;
                default: discipline += points; break;
            }
        }

        var total = Math.Clamp(technical + market + liquidity + discipline, 0, 100);

        // Quá mù để giao dịch. Chuẩn hoá ngưỡng theo điểm khả dụng là đúng, nhưng nếu để nó chạy
        // không giới hạn thì một tài khoản mất gần hết nguồn dữ liệu vẫn vào lệnh đều đặn chỉ
        // nhờ vài tiêu chí còn sống — và tỉ lệ phần trăm sẽ trông rất đẹp vì mẫu số đã teo lại.
        //
        // So theo TỈ LỆ PHỦ, không theo số điểm tuyệt đối: thang điểm suy ra từ bộ tiêu chí đang
        // đăng ký, nên một ngưỡng tuyệt đối sẽ tự sai đi mỗi lần bộ tiêu chí thay đổi.
        //
        // `_totalMaxPoints == 0` nghĩa là chưa có tiêu chí cộng điểm nào — đó là lỗi cấu hình,
        // không phải mù dữ liệu, và nó đã có đường xử lý riêng (điểm 0 ⟹ dưới mọi ngưỡng).
        var coveragePercent = _totalMaxPoints <= 0 ? 100m : (decimal)availableMax / _totalMaxPoints * 100m;
        var minCoverage = context.Settings.MinDataCoveragePercent;

        if (_totalMaxPoints > 0 && coveragePercent < minCoverage)
        {
            return new ScoringOutcome(
                TotalScore: 0,
                TechnicalScore: technical,
                MarketScore: market,
                LiquidityScore: liquidity,
                DisciplinePenalty: discipline,
                IsVetoed: true,
                VetoReason: Domain.Enums.VetoReason.InsufficientData,
                VetoDetail:
                    $"Chỉ đo được {availableMax}/{_totalMaxPoints} điểm ({coveragePercent:N1}%), " +
                    $"dưới mức phủ tối thiểu {minCoverage:N0}% — quá nhiều nguồn dữ liệu chết để kết luận điều gì.",
                Lines: lines,
                TotalMaxPoints: _totalMaxPoints,
                AvailableMaxPoints: availableMax,
                DirectionalScore: directional,
                DirectionalMaxPoints: _directionalMaxPoints);
        }

        return new ScoringOutcome(total, technical, market, liquidity, discipline,
            IsVetoed: false, VetoReason: null, VetoDetail: null, Lines: lines,
            TotalMaxPoints: _totalMaxPoints, AvailableMaxPoints: availableMax,
            DirectionalScore: directional, DirectionalMaxPoints: _directionalMaxPoints);
    }

    /// <summary>
    /// Kẹp điểm về đúng miền cho phép của nhóm.
    /// </summary>
    /// <remarks>
    /// Vòng tổng hợp không tin tiêu chí. Một tiêu chí trả vượt trần hay trả điểm dương cho
    /// nhóm chỉ-trừ là lỗi lập trình, nhưng để nó lọt sẽ làm tổng điểm sai theo cách rất khó
    /// truy: phiếu vẫn hợp lệ, chỉ là con số bị thổi lên. Kẹp ở đây biến lỗi đó thành vô hại,
    /// còn test riêng cho từng tiêu chí lo việc bắt nó.
    /// </remarks>
    private static int Normalise(IScoreCriterion criterion, CriterionResult result)
    {
        // FR-006: thiếu dữ liệu ⟹ 0 điểm. Không phải điểm trung bình, không phải điểm tối đa.
        if (!result.DataAvailable) return 0;

        return criterion.Group == ScoreGroup.Discipline
            ? Math.Min(0, result.AwardedPoints)
            : Math.Clamp(result.AwardedPoints, 0, criterion.MaxPoints);
    }
}
