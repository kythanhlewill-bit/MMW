using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class AccountsController : Controller
{
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITradeResultSyncService _syncService;

    public AccountsController(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IUnitOfWork unitOfWork,
        ITradeResultSyncService syncService)
    {
        _accounts = accounts;
        _riskSettings = riskSettings;
        _unitOfWork = unitOfWork;
        _syncService = syncService;
    }

    public async Task<IActionResult> Index()
    {
        var accounts = (await _accounts.GetAllAsync()).OrderBy(a => a.Name).ToList();
        return View(accounts);
    }

    [HttpGet]
    public IActionResult Create()
    {
        var model = new AccountFormModel { IsActive = true, Currency = "USDT", Broker = Broker.Binance };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccountFormModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var account = new TradingAccount
        {
            Name = model.Name,
            Broker = model.Broker,
            Currency = model.Currency,
            InitialBalance = model.InitialBalance,
            CurrentBalance = model.InitialBalance,
            IsActive = model.IsActive,
            ApiKey = model.ApiKey,
            ApiSecret = model.ApiSecret,
            RiskSetting = new RiskSetting(),
        };

        await _accounts.AddAsync(account);
        await _unitOfWork.CommitAsync();
        TempData["Message"] = $"Đã tạo tài khoản \"{model.Name}\".";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id)
    {
        var account = await _accounts.FindAsync(id);
        if (account is null) return NotFound();

        var model = new AccountFormModel
        {
            Id = account.Id,
            Name = account.Name,
            Broker = account.Broker,
            Currency = account.Currency,
            InitialBalance = account.InitialBalance,
            CurrentBalance = account.CurrentBalance,
            IsActive = account.IsActive,
            ApiKey = account.ApiKey,
            ApiSecret = account.ApiSecret,
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AccountFormModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var account = await _accounts.FindAsync(model.Id);
        if (account is null) return NotFound();

        account.Name = model.Name;
        account.Broker = model.Broker;
        account.Currency = model.Currency;
        account.InitialBalance = model.InitialBalance;
        account.CurrentBalance = model.CurrentBalance;
        account.IsActive = model.IsActive;
        account.ApiKey = model.ApiKey;
        account.ApiSecret = string.IsNullOrWhiteSpace(model.ApiSecret) ? account.ApiSecret : model.ApiSecret;

        _accounts.Update(account);
        await _unitOfWork.CommitAsync();
        TempData["Message"] = $"Đã cập nhật tài khoản \"{account.Name}\".";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(long id)
    {
        var account = await _accounts.FindAsync(id);
        if (account is null) return NotFound();

        account.IsActive = !account.IsActive;
        _accounts.Update(account);
        await _unitOfWork.CommitAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncNow(long id)
    {
        var result = await _syncService.SyncAccountAsync(id);
        TempData["Message"] = $"Đồng bộ xong: {result.Synced} lệnh đã cập nhật, {result.Skipped} bỏ qua, {result.Failed} lỗi.";
        return RedirectToAction(nameof(Index));
    }
}
