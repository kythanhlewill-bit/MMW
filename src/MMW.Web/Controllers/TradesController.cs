using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;

namespace MMW.Web.Controllers;

/// <summary>
/// Controller mẫu — luồng Controller → Service → Repository.
/// </summary>
public class TradesController : Controller
{
    private readonly ITradeService _tradeService;

    public TradesController(ITradeService tradeService)
    {
        _tradeService = tradeService;
    }

    public async Task<IActionResult> Index()
    {
        var trades = await _tradeService.GetAllAsync();
        return View(trades);
    }
}
