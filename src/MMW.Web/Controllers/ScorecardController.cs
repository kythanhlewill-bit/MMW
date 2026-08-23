using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.DbContext;
using MMW.Domain.Enums;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

/// <summary>
/// Phiếu chấm điểm: xem mọi lần đánh giá, kể cả những lần kết luận là không vào lệnh.
/// </summary>
/// <remarks>
/// Mục tiêu SC-013: tra được lý do một cơ hội bị từ chối trong dưới 30 giây. Vì vậy bộ lọc
/// theo lý do từ chối nằm ngay ở đầu trang, và chi tiết điểm từng tiêu chí mở ra tại chỗ chứ
/// không phải qua thêm một lần bấm sang trang khác.
/// </remarks>
public class ScorecardController : Controller
{
    private const int PageSize = 50;

    private readonly MmwDbContext _db;

    public ScorecardController(MmwDbContext db) => _db = db;

    public async Task<IActionResult> Index(
        string? symbol, VetoReason? veto, ScorecardOutcome? outcome, int? minScore,
        TradeStyle? style, CancellationToken ct)
    {
        var query = _db.EntryScorecards.AsNoTracking().Where(c => !c.IsBacktest);

        // Lọc theo nhóm lệnh. Hai bộ luật chấm trên hai nguồn chiều khác nhau, nên trộn phiếu của
        // chúng vào một danh sách sẽ khiến mọi câu hỏi kiểu "vì sao hôm nay không vào lệnh" trả
        // lời bằng lý do của bộ luật KHÔNG chạy.
        if (style is { } st) query = query.Where(c => c.Style == st);

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(c => c.Symbol == symbol.ToUpperInvariant());

        if (veto is not null) query = query.Where(c => c.VetoReason == veto);
        if (outcome is not null) query = query.Where(c => c.Outcome == outcome);

        // Lọc điểm lớn hơn n. Phiếu bị veto ghi TotalScore = 0 theo hợp đồng của bộ chấm, nên đặt
        // n ≥ 0 cũng đồng thời loại chúng — đó là điều người lọc theo điểm đang muốn.
        if (minScore is { } floor) query = query.Where(c => c.TotalScore > floor);

        var model = new ScorecardListViewModel
        {
            Symbol = symbol,
            Veto = veto,
            Outcome = outcome,
            MinScore = minScore,
            Style = style,
            StyleCounts = await _db.EntryScorecards.AsNoTracking()
                .Where(c => !c.IsBacktest)
                .GroupBy(c => c.Style)
                .Select(g => new { Style = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Style, x => x.Count, ct),
            Items = await query
                .Include(c => c.Lines)
                .OrderByDescending(c => c.EvaluatedAtUtc)
                .Take(PageSize)
                .ToListAsync(ct),

            // Bảng xếp hạng lý do từ chối — Nguyên tắc IV: "3 tháng qua lý do phổ biến nhất
            // là gì" là câu hỏi trader sẽ hỏi, nên nó phải có sẵn câu trả lời.
            VetoCounts = await _db.EntryScorecards.AsNoTracking()
                .Where(c => !c.IsBacktest && c.VetoReason != null)
                .GroupBy(c => c.VetoReason!.Value)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionaryAsync(x => x.Reason, x => x.Count, ct),

            ZeroPointCriteria = await _db.EntryScorecardLines.AsNoTracking()
                .Where(l => l.AwardedPoints == 0 && l.MaxPoints > 0)
                .GroupBy(l => l.CriterionKey)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct),
        };

        return View(model);
    }
}
