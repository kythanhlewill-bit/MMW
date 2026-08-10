using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Application.Backtest;
using MMW.Domain.DbContext;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

/// <summary>
/// Xem và chạy kiểm thử lịch sử.
/// </summary>
/// <remarks>
/// Màn hình này hiển thị <c>Limitations</c> NGAY CẠNH các con số kết quả, không giấu xuống
/// cuối trang. Một báo cáo kiểm thử không nêu hạn chế của chính nó sẽ được đọc như một lời
/// hứa — và đó chính là cách người ta thuyết phục bản thân bật giao dịch thật quá sớm.
///
/// Việc CHẠY kiểm thử không nằm ở đây: nó cần thay hai cổng <c>IClock</c> và
/// <c>IMarketDataProvider</c> trong phạm vi một scope riêng, còn scope của request web thì
/// đang gắn với đồng hồ thật. Chạy bằng lệnh CLI <c>backtest</c>, xem kết quả ở đây.
/// </remarks>
public class BacktestController : Controller
{
    private const int PageSize = 30;

    private readonly MmwDbContext _db;
    private readonly IKlineArchiveReader _archive;

    public BacktestController(MmwDbContext db, IKlineArchiveReader archive)
    {
        _db = db;
        _archive = archive;
    }

    public async Task<IActionResult> Index(long? runId, CancellationToken ct)
    {
        var runs = await _db.BacktestRuns.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(PageSize)
            .ToListAsync(ct);

        var selected = runId is null
            ? runs.FirstOrDefault()
            : runs.FirstOrDefault(r => r.Id == runId) ?? await _db.BacktestRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, ct);

        return View(new BacktestViewModel
        {
            Runs = runs,
            Selected = selected,
            ArchiveCandleCount = await _db.KlineArchives.CountAsync(ct),
            ArchiveFundingCount = await _db.FundingRateArchives.CountAsync(ct),
            ArchiveSymbols = await _db.KlineArchives.AsNoTracking()
                .Select(k => k.Symbol).Distinct().OrderBy(s => s).ToListAsync(ct),
        });
    }

    /// <summary>Kiểm tra kho có liền mạch không, trước khi chạy.</summary>
    [HttpGet]
    public async Task<IActionResult> Gaps(string symbol, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var gaps = await _archive.FindGapsAsync(
            symbol.ToUpperInvariant(), "15m",
            DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(toUtc, DateTimeKind.Utc), ct);

        return Json(new
        {
            count = gaps.Count,
            gaps = gaps.Take(20).Select(g => new { from = g.From, to = g.To }),
        });
    }
}
