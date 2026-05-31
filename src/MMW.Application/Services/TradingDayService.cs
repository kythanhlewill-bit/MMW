using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class TradingDayService : ITradingDayService
{
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<TradingDay> _tradingDays;
    private readonly IUnitOfWork _unitOfWork;

    public TradingDayService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<TradingDay> tradingDays,
        IUnitOfWork unitOfWork)
    {
        _trades = trades;
        _accounts = accounts;
        _tradingDays = tradingDays;
        _unitOfWork = unitOfWork;
    }

    public async Task<TradingDay> RecomputeAndSaveAsync(long accountId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Không tìm thấy TradingAccount {accountId}.");

        // Các lệnh đã thực sự vào (Open/Closed) trong ngày.
        var dayTrades = (await _trades.FindListAsync(t => t.TradingAccountId == accountId))
            .Where(t => (t.Status == TradeStatus.Open || t.Status == TradeStatus.Closed)
                        && DateOnly.FromDateTime(t.OpenedAt ?? t.CreatedDate) == date)
            .OrderBy(t => t.OpenedAt ?? t.CreatedDate)
            .ThenBy(t => t.Id)
            .ToList();

        var closed = dayTrades.Where(t => t.RealizedPnl.HasValue).ToList();

        var grossProfit = closed.Where(t => t.RealizedPnl > 0m).Sum(t => t.RealizedPnl!.Value);
        var grossLoss = closed.Where(t => t.RealizedPnl < 0m).Sum(t => t.RealizedPnl!.Value); // âm

        // Chuỗi thua liên tiếp dài nhất trong ngày.
        var maxStreak = 0;
        var run = 0;
        foreach (var t in closed)
        {
            if (t.RealizedPnl < 0m) { run++; maxStreak = Math.Max(maxStreak, run); }
            else run = 0;
        }

        var existing = await FindDayAsync(accountId, date);
        var day = existing ?? new TradingDay { TradingAccountId = accountId, Date = date };

        day.TradeCount = dayTrades.Count;
        day.WinCount = closed.Count(t => t.RealizedPnl > 0m);
        day.LossCount = closed.Count(t => t.RealizedPnl < 0m);
        day.GrossProfit = grossProfit;
        day.GrossLoss = grossLoss;
        day.NetPnl = grossProfit + grossLoss;
        day.MaxConsecutiveLosses = maxStreak;
        day.TotalRiskPercent = dayTrades.Sum(t => t.RiskPercent ?? 0m);
        // Ước lượng vốn đầu ngày = vốn hiện tại trừ PnL trong ngày (đủ dùng cho MVP).
        day.StartingEquity = account.CurrentBalance - day.NetPnl;

        if (existing is null)
            await _tradingDays.AddAsync(day);
        else
            _tradingDays.Update(day);

        await _unitOfWork.CommitAsync(cancellationToken);
        return day;
    }

    private async Task<TradingDay?> FindDayAsync(long accountId, DateOnly date)
    {
        // FirstOrDefault dùng AsNoTracking; cần bản tracking để Update → truy vấn qua Queryable.
        var list = await _tradingDays.FindListAsync(d => d.TradingAccountId == accountId && d.Date == date);
        if (list.Count == 0)
            return null;

        // Lấy lại bản tracking theo Id để update an toàn.
        return await _tradingDays.FindAsync(list[0].Id);
    }
}
