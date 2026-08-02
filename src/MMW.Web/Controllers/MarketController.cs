using Hangfire;
using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class MarketController : Controller
{
    private const int HistoryPageSize = 200;

    private readonly IBaseRepository<MarketSnapshot> _snapshots;
    private readonly IBaseRepository<IndicatorRecord> _history;

    public MarketController(
        IBaseRepository<MarketSnapshot> snapshots,
        IBaseRepository<IndicatorRecord> history)
    {
        _snapshots = snapshots;
        _history = history;
    }

    public async Task<IActionResult> Index()
    {
        var data = (await _snapshots.GetAllAsync())
            .OrderBy(s => s.Symbol)
            .ToList();
        return View(data);
    }

    public IActionResult History(string? symbol, int page = 1, int pageSize = 20)
    {
        var query = _history.GetAll();
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            symbol = symbol.Trim().ToUpperInvariant();
            query = query.Where(r => r.Symbol == symbol);
        }

        var pager = PagerModel.Build(page, pageSize, query.Count());
        var records = query
            .OrderByDescending(r => r.Id)
            .Skip((pager.CurrentPage - 1) * pager.PageSize)
            .Take(pager.PageSize)
            .ToList();

        ViewBag.Pager = pager;
        return View(new IndicatorHistoryViewModel { Symbol = symbol, Records = records });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ScanNow()
    {
        // Đẩy job quét chạy nền ngay lập tức.
        BackgroundJob.Enqueue<IMarketScanService>(job => job.ScanAllAsync(CancellationToken.None));
        TempData["Message"] = "Đã kích hoạt quét thị trường. Làm mới sau vài giây để xem kết quả.";
        return RedirectToAction(nameof(Index));
    }
}
