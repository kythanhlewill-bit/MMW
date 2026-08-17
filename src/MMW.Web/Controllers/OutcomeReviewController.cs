using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Application.Services;
using MMW.Domain.DbContext;
using MMW.Domain.Enums;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

/// <summary>
/// Kết cục phiếu: các cổng veto đã chặn đúng hay chặn nhầm, đo bằng giá thật sau đó.
/// </summary>
/// <remarks>
/// Trang này trả lời một câu duy nhất mà không màn hình nào khác trả lời được: <b>nếu cổng không
/// chặn thì sao?</b> Mọi phiếu bị từ chối vẫn được cho chạy tiếp trên kho nến, nên tập hợp "những
/// lệnh đã không xảy ra" có kết quả đo được thay vì chỉ có phỏng đoán.
///
/// Cột <c>netR</c> mới là cột kết luận. Bảng nào cũng để <c>grossR</c> ngay cạnh <c>netR</c> có
/// chủ ý: khoảng cách giữa hai cột chính là chi phí, và ở stop hẹp khoảng cách đó lớn tới mức
/// đảo ngược kết luận. Đọc một mình <c>grossR</c> là tự lừa.
/// </remarks>
public sealed class OutcomeReviewController : Controller
{
    private const int RecentLimit = 100;

    private readonly MmwDbContext _db;

    public OutcomeReviewController(MmwDbContext db) => _db = db;

    public async Task<IActionResult> Index(
        DateOnly? fromDate, DateOnly? toDate, string? symbol, VetoReason? veto, int? minScore,
        CancellationToken ct)
    {
        // Người dùng chọn NGÀY VIỆT NAM. Trước đây hai ô này nhận thẳng ngày UTC, nên chọn "13/08"
        // thật ra lấy khoảng 07:00 sáng 13/08 đến 07:00 sáng 14/08 giờ VN — hai nửa của hai ngày
        // khác nhau. Sai lệch đó không bao giờ tự lộ ra, nó chỉ làm mọi con số trên trang hơi lệch.
        var todayVn = ScorecardOutcomeViewModel.TodayVn(DateTime.UtcNow);
        var toVn = toDate ?? todayVn;
        var fromVn = fromDate ?? toVn.AddDays(-30);
        if (fromVn > toVn) (fromVn, toVn) = (toVn, fromVn);

        var from = ScorecardOutcomeViewModel.VnDayStartUtc(fromVn);
        var to = ScorecardOutcomeViewModel.VnDayEndUtc(toVn);

        var normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

        // Chỉ lấy bản ghi của luật phân giải ĐANG dùng. Trộn hai phiên bản luật vào một phép trung
        // bình là cách chắc chắn nhất để một lần sửa horizon trông như một thay đổi của thị trường.
        var query = _db.ScorecardOutcomeReviews.AsNoTracking()
            .Where(r => r.ResolverVersion == ScorecardOutcomeReviewService.ResolverVersion)
            .Join(_db.EntryScorecards.AsNoTracking(),
                r => r.EntryScorecardId,
                c => c.Id,
                (r, c) => new { r, c })
            .Where(x => !x.c.IsBacktest
                        && x.c.EvaluatedAtUtc >= from
                        && x.c.EvaluatedAtUtc <= to);

        if (normalizedSymbol is not null) query = query.Where(x => x.c.Symbol == normalizedSymbol);
        if (veto is not null) query = query.Where(x => x.c.VetoReason == veto);

        // Lọc điểm lớn hơn n, giống trang Phiếu chấm điểm. Đặt TRƯỚC mọi phép gộp có chủ ý: mọi
        // con số trên trang — KPI, ba bảng nhóm, ngưỡng hoà vốn — đều phải nói về cùng một tập
        // phiếu. Lọc sau khi đã tính sẽ cho ra một trang mà bảng này mâu thuẫn bảng kia.
        if (minScore is { } floor) query = query.Where(x => x.c.TotalScore > floor);

        var rows = await query
            .OrderByDescending(x => x.c.EvaluatedAtUtc)
            .Select(x => new OutcomeRow(
                x.r.Id,
                x.c.Id,
                x.c.Symbol,
                x.c.EvaluatedAtUtc,
                x.c.Direction,
                x.c.VetoReason,
                x.c.Outcome,
                x.c.TotalScore,
                x.r.Outcome,
                x.r.BarsToExit,
                x.r.GrossR,
                x.r.NetR,
                x.r.StopDistancePercent,
                x.r.MaxFavorableExcursionR,
                x.r.MaxAdverseExcursionR,
                // Đúng ba mức mà ScorecardOutcomeReviewService đã mô phỏng — xem OutcomeRow.
                x.c.SuggestedEntry,
                x.c.SuggestedStopLoss,
                x.c.SuggestedFirstTakeProfit ?? x.c.SuggestedTakeProfit))
            .ToListAsync(ct);

        var wins = rows.Where(r => r.NetR > 0m).Select(r => r.NetR).ToList();
        var losses = rows.Where(r => r.NetR < 0m).Select(r => -r.NetR).ToList();

        var model = new ScorecardOutcomeViewModel
        {
            FromDateVn = fromVn,
            ToDateVn = toVn,
            FromUtc = from,
            ToUtc = to,
            Symbol = normalizedSymbol,
            Veto = veto,
            MinScore = minScore,
            ResolverVersion = ScorecardOutcomeReviewService.ResolverVersion,

            Overall = OutcomeStat.From("Tất cả", rows),
            AvgWinR = wins.Count == 0 ? 0m : wins.Sum() / wins.Count,
            AvgLossR = losses.Count == 0 ? 0m : losses.Sum() / losses.Count,

            ByVeto = rows
                .GroupBy(r => r.Veto)
                .Select(g => OutcomeStat.From(
                    g.Key is null ? "Không bị chặn" : ScorecardListViewModel.VetoLabel(g.Key.Value),
                    g.ToList()))
                .OrderByDescending(s => s.Count)
                .ToList(),

            ByStopBucket = rows
                .GroupBy(r => ScorecardOutcomeViewModel.StopBucket(r.StopDistancePercent))
                .Select(g => OutcomeStat.From(g.Key, g.ToList()))
                .OrderBy(s => ScorecardOutcomeViewModel.StopBucketOrder(s.Label))
                .ToList(),

            BySymbol = rows
                .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => OutcomeStat.From(g.Key, g.ToList()))
                .OrderByDescending(s => s.Count)
                .ToList(),

            Recent = rows.Take(RecentLimit).ToList(),

            KnownSymbols = await _db.EntryScorecards.AsNoTracking()
                .Where(c => !c.IsBacktest)
                .Select(c => c.Symbol)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(ct),

            KnownVetoes = rows
                .Where(r => r.Veto is not null)
                .Select(r => r.Veto!.Value)
                .Distinct()
                .OrderBy(v => v.ToString(), StringComparer.Ordinal)
                .ToList(),

            LastResolvedAtUtc = await _db.ScorecardOutcomeReviews.AsNoTracking()
                .Where(r => r.ResolverVersion == ScorecardOutcomeReviewService.ResolverVersion)
                .MaxAsync(r => (DateTime?)r.ResolvedAtUtc, ct),

            // Hàng đợi, không phải lỗi: phiếu mới chấm chưa có đủ 24 giờ nến phía sau. Hiện con số
            // này để một bảng thưa không bị hiểu nhầm là job chết.
            PendingCount = await _db.EntryScorecards.AsNoTracking()
                .CountAsync(c => !c.IsBacktest
                                 && c.Direction != null
                                 && c.SuggestedEntry != null
                                 && c.SuggestedStopLoss != null
                                 && (c.SuggestedFirstTakeProfit ?? c.SuggestedTakeProfit) != null
                                 && !_db.ScorecardOutcomeReviews.Any(
                                     r => r.EntryScorecardId == c.Id
                                          && r.ResolverVersion == ScorecardOutcomeReviewService.ResolverVersion),
                    ct),
        };

        return View(model);
    }
}
