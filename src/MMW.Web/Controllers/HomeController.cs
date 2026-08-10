using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Abstractions;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Discipline;
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
    private readonly IDisciplineGateRunner _gates;
    private readonly ITraderStatisticsProvider _traderStats;
    private readonly IDailyPlanService _dailyPlan;
    private readonly IBaseRepository<EngineSetting> _engineSettings;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IClock _clock;
    private readonly LiveTradingOptions _liveTrading;

    public HomeController(
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Trade> trades,
        IBaseRepository<Flag> flags,
        ISettingsService settings,
        IExchangeAccountProviderFactory exchangeFactory,
        IDisciplineGateRunner gates,
        ITraderStatisticsProvider traderStats,
        IDailyPlanService dailyPlan,
        IBaseRepository<EngineSetting> engineSettings,
        IBaseRepository<RiskSetting> riskSettings,
        IClock clock,
        IOptions<LiveTradingOptions> liveTrading)
    {
        _accounts = accounts;
        _trades = trades;
        _flags = flags;
        _settings = settings;
        _exchangeFactory = exchangeFactory;
        _gates = gates;
        _traderStats = traderStats;
        _dailyPlan = dailyPlan;
        _engineSettings = engineSettings;
        _riskSettings = riskSettings;
        _clock = clock;
        _liveTrading = liveTrading.Value;
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
                    var provider = _exchangeFactory.Create(account.ApiKey, account.ApiSecret, _liveTrading.UseTestnet);
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

            await FillDisciplineStatusAsync(vm, account);
        }

        return View(vm);
    }

    /// <summary>
    /// Chạy bộ rào kỷ luật ở chế độ CHỈ ĐỌC để hiện trạng thái hiện tại (T125).
    /// </summary>
    /// <remarks>
    /// Chạy lại gate ở đây thay vì đọc kết quả đã lưu là có chủ ý: trạng thái kỷ luật thay đổi
    /// theo từng phút (cửa sổ trả thù hết hạn, sang ngày mới), nên một con số lưu từ chu kỳ
    /// chấm điểm gần nhất sẽ hiển thị sai ngay khi trang được mở lại.
    ///
    /// Không có kế hoạch ngày thì không hiện gì — chưa có kế hoạch là một trạng thái khác hẳn,
    /// và trang kế hoạch ngày đã nói về nó rồi.
    /// </remarks>
    private async Task FillDisciplineStatusAsync(DashboardViewModel vm, TradingAccount account)
    {
        var engineSetting = await _engineSettings.FirstOrDefaultAsync(s => s.TradingAccountId == account.Id);
        if (engineSetting is null) return;

        var plan = await _dailyPlan.GetCurrentAsync(account.Id);
        if (plan is null) return;

        var riskSetting = await _riskSettings.FirstOrDefaultAsync(r => r.TradingAccountId == account.Id)
                          ?? new RiskSetting { TradingAccountId = account.Id };

        var utcNow = _clock.UtcNow;
        var stats = await _traderStats.GetAsync(account.Id, utcNow);

        // Bảng này hỏi "hiện giờ tôi có được phép giao dịch không", chưa có mã và chiều cụ thể.
        // Mã rỗng là câu trả lời trung thực cho câu hỏi đó, không phải giá trị tạm:
        //   • `discipline.open_position` không khớp mã nào nên chỉ còn kiểm trần số vị thế đồng
        //     thời — đúng phần mang tính toàn tài khoản của nó.
        //   • `discipline.correlated_exposure` không đo được tương quan nên cho qua kèm lý do
        //     nói rõ như vậy.
        // Điền một mã giả để "cho có" sẽ khiến bảng khẳng định những điều chưa ai hỏi.
        var outcome = _gates.Run(new DisciplineContext
        {
            TradingAccountId = account.Id,
            Symbol = string.Empty,
            Direction = TradeDirection.Long,
            EvaluatedAtUtc = utcNow,
            PlannedRiskPercent = riskSetting.MaxRiskPerTradePercent,
            DailyPlan = plan,
            Settings = engineSetting,
            RiskSettings = riskSetting,
            Stats = stats,
        });

        vm.IsBlocked = outcome.Aggregate.IsBlocked;
        vm.BlockReason = outcome.Aggregate.Detail;
        vm.DisciplineSizeMultiplier = outcome.Aggregate.SizeMultiplier;

        vm.DisciplineGates = outcome.Lines
            .Select(l => new DisciplineStatusRow(
                l.Key,
                l.Result.Action.ToString(),
                l.Result.Reason,
                l.Result.Action is GateAction.BlockTrade or GateAction.StopForDay))
            .ToList();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
