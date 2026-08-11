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
        DateTime? fromUtc, DateTime? toUtc, string? symbol, VetoReason? veto, CancellationToken ct)
    {
        var to = NormalizeUtc(toUtc ?? DateTime.UtcNow);
        var from = NormalizeUtc(fromUtc ?? to.AddDays(-30));
        if (from > to) (from, to) = (to, from);

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
                x.r.MaxAdverseExcursionR))
            .ToListAsync(ct);

        var wins = rows.Where(r => r.NetR > 0m).Select(r => r.NetR).ToList();
        var losses = rows.Where(r => r.NetR < 0m).Select(r => -r.NetR).ToList();

        var model = new ScorecardOutcomeViewModel
        {
            FromUtc = from,
            ToUtc = to,
            Symbol = normalizedSymbol,
            Veto = veto,
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

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
