using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class HomeController : Controller
{
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<Flag> _flags;
    private readonly ISettingsService _settings;
    private readonly IExchangeAccountProviderFactory _exchangeFactory;

    public HomeController(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Trade> trades,
        IBaseRepository<Flag> flags,
        ISettingsService settings,
        IExchangeAccountProviderFactory exchangeFactory)
    {
        _accounts = accounts;
        _trades = trades;
        _flags = flags;
        _settings = settings;
        _exchangeFactory = exchangeFactory;
    }

    public async Task<IActionResult> Index(long? accountId)
    {
        var allAccounts = (await _accounts.GetAllAsync()).OrderBy(a => a.Name).ToList();

        TradingAccount? account;
        if (accountId.HasValue)
        {
            account = allAccounts.FirstOrDefault(a => a.Id == accountId.Value);
        }
        else
        {
            var appSetting = await _settings.GetAppSettingAsync();
            account = appSetting.DefaultTradingAccountId.HasValue
                ? allAccounts.FirstOrDefault(a => a.Id == appSetting.DefaultTradingAccountId.Value)
                : null;
            account ??= allAccounts.FirstOrDefault(a => a.IsActive);
        }

        var vm = new DashboardViewModel { Accounts = allAccounts, SelectedAccountId = account?.Id };

        if (account is not null)
        {
            vm.AccountName = account.Name;
            vm.Currency = account.Currency;
            vm.Balance = account.CurrentBalance;

            // Lấy số dư thực tế từ Binance Futures nếu tài khoản có API key.
            if (!string.IsNullOrWhiteSpace(account.ApiKey) && !string.IsNullOrWhiteSpace(account.ApiSecret))
            {
                try
                {
                    var provider = _exchangeFactory.Create(account.ApiKey, account.ApiSecret);
                    vm.LiveBalance = await provider.GetFuturesUsdtBalanceAsync();
                }
                catch (Exception ex)
                {
                    vm.LiveBalanceFetchError = ex.Message.Length > 100 ? ex.Message[..100] : ex.Message;
                }
            }
            else
            {
                vm.LiveBalanceFetchError = "Chưa cấu hình API key Binance.";
            }
            vm.TotalTrades = await _trades.CountAsync(t => t.TradingAccountId == account.Id);
            vm.OpenTrades = await _trades.CountAsync(t => t.TradingAccountId == account.Id && t.Status == TradeStatus.Open);
            vm.ClosedTrades = await _trades.CountAsync(t => t.TradingAccountId == account.Id && t.Status == TradeStatus.Closed);
            vm.WinTrades = await _trades.CountAsync(t => t.TradingAccountId == account.Id && t.Outcome == TradeOutcome.Win);
            vm.LossTrades = await _trades.CountAsync(t => t.TradingAccountId == account.Id && t.Outcome == TradeOutcome.Loss);

            var closedTrades = await _trades.FindListAsync(t => t.TradingAccountId == account.Id && t.RealizedPnl != null);
            vm.TotalPnl = closedTrades.Sum(t => t.RealizedPnl ?? 0);

            vm.TotalFlags = await _flags.CountAsync(f => f.TradingAccountId == account.Id);
            vm.CriticalFlags = await _flags.CountAsync(f => f.TradingAccountId == account.Id && f.Severity == FlagSeverity.Critical);
        }

        return View(vm);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
