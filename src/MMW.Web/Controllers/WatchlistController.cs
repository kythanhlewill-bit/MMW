using Microsoft.AspNetCore.Mvc;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Web.Controllers;

public class WatchlistController : Controller
{
    private readonly IBaseRepository<WatchItem> _watchItems;
    private readonly IUnitOfWork _unitOfWork;

    public WatchlistController(IBaseRepository<WatchItem> watchItems, IUnitOfWork unitOfWork)
    {
        _watchItems = watchItems;
        _unitOfWork = unitOfWork;
    }

    public async Task<IActionResult> Index()
    {
        var items = (await _watchItems.GetAllAsync())
            .OrderBy(w => w.Symbol).ThenBy(w => w.Interval)
            .ToList();
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string symbol, string interval)
    {
        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        interval = string.IsNullOrWhiteSpace(interval) ? "1h" : interval.Trim();

        if (string.IsNullOrEmpty(symbol))
        {
            TempData["Error"] = "Vui lòng nhập symbol.";
            return RedirectToAction(nameof(Index));
        }

        var exists = await _watchItems.AnyAsync(w => w.Symbol == symbol && w.Interval == interval);
        if (exists)
        {
            TempData["Error"] = $"{symbol} ({interval}) đã có trong watchlist.";
            return RedirectToAction(nameof(Index));
        }

        await _watchItems.AddAsync(new WatchItem { Symbol = symbol, Interval = interval, IsActive = true });
        await _unitOfWork.CommitAsync();
        TempData["Message"] = $"Đã thêm {symbol} ({interval}).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(long id)
    {
        var item = await _watchItems.FindAsync(id);
        if (item is not null)
        {
            item.IsActive = !item.IsActive;
            _watchItems.Update(item);
            await _unitOfWork.CommitAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id)
    {
        var item = await _watchItems.FindAsync(id);
        if (item is not null)
        {
            _watchItems.Remove(item);
            await _unitOfWork.CommitAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
