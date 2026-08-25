using Microsoft.EntityFrameworkCore;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Trading.Discipline;

public interface ITraderStatisticsProvider
{
    Task<TraderStatistics> GetAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

/// <summary>
/// Tính sẵn thống kê hành vi để gate và tiêu chí không phải chạm cơ sở dữ liệu.
/// </summary>
/// <remarks>
/// Ngày giao dịch neo tại 00:00 UTC (FR-024). Mọi bộ đếm "trong ngày" — số lệnh đã vào, phần
/// trăm lỗ — reset tại mốc đó, và vị thế đang mở KHÔNG bị đụng tới: reset là reset bộ đếm,
/// không phải đóng lệnh.
/// </remarks>
public sealed class TraderStatisticsProvider : ITraderStatisticsProvider
{
    /// <summary>Số khung giờ tệ nhất bị đánh dấu. Là ĐỊNH NGHĨA của "top-2 giờ thua nhiều nhất".</summary>
    private const int WorstHourCount = 2;

    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<EntryScorecard> _scorecards;

    public TraderStatisticsProvider(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<EngineSetting> settings,
        IBaseRepository<EntryScorecard> scorecards)
    {
        _trades = trades;
        _accounts = accounts;
        _settings = settings;
        _scorecards = scorecards;
    }

    public async Task<TraderStatistics> GetAsync(
        long tradingAccountId, DateTime utcNow, CancellationToken ct = default)
    {
        var lookback = await _settings
            .Get(s => s.TradingAccountId == tradingAccountId)
            .AsNoTracking()
            .Select(s => (int?)s.OversizeLookbackTrades)
            .FirstOrDefaultAsync(ct) ?? 20;

        var closed = await _trades
            .Get(t => t.TradingAccountId == tradingAccountId
                      && t.Status == TradeStatus.Closed
                      && t.ClosedAt != null)
            .AsNoTracking()
            .Select(t => new
            {
                t.ClosedAt,
                t.OpenedAt,
                t.Outcome,
                t.RealizedPnl,
                t.RiskPercent,
                t.EntryScorecardId,
                t.Style,
            })
            .ToListAsync(ct);

        var dayStart = utcNow.Date;

        // Đọc về danh sách nhóm thay vì đếm thẳng: cùng một lượt truy vấn phải trả lời được cả
        // câu hỏi toàn tài khoản lẫn câu hỏi từng nhóm, vì hai bộ luật chạy song song có hạn mức
        // riêng và đếm chung sẽ để bộ luật này bị khoá bởi hoạt động của bộ luật kia.
        var openedTodayByStyle = await _trades
            .Get(t => t.TradingAccountId == tradingAccountId
                      && t.Status != TradeStatus.Cancelled
                      && t.Status != TradeStatus.Planned
                      && t.OpenedAt != null
                      && t.OpenedAt >= dayStart
                      && t.OpenedAt < dayStart.AddDays(1))
            .AsNoTracking()
            .GroupBy(t => t.Style)
            .Select(g => new { Style = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var tradesTodayByStyle = openedTodayByStyle.ToDictionary(x => x.Style, x => x.Count);
        var tradesToday = openedTodayByStyle.Sum(x => x.Count);

        // Vị thế ĐANG MỞ — khác hẳn `tradesToday`, thứ chỉ đếm số lệnh đã vào trong ngày. Không
        // có danh sách này thì không gate nào biết hệ thống đang cầm gì, và một setup tốt chấm
        // đạt suốt nhiều nến liền sẽ được vào nhiều lần trên cùng một ý tưởng.
        var openPositions = await _trades
            .Get(t => t.TradingAccountId == tradingAccountId && t.Status == TradeStatus.Open)
            .AsNoTracking()
            .Select(t => new { t.Symbol, t.Direction, t.RiskPercent, t.Style })
            .ToListAsync(ct);

        var ordered = closed.OrderByDescending(t => t.ClosedAt!.Value).ToList();
        var orderedToday = ordered
            .Where(t => t.ClosedAt!.Value >= dayStart && t.ClosedAt.Value < dayStart.AddDays(1))
            .ToList();

        var streak = 0;
        foreach (var t in ordered)
        {
            if (t.Outcome != TradeOutcome.Loss) break;
            streak++;
        }

        var streakToday = 0;
        foreach (var t in orderedToday)
        {
            if (t.Outcome != TradeOutcome.Loss) break;
            streakToday++;
        }

        // Cùng phép đếm, áp riêng từng nhóm. Phải đi từ danh sách ĐÃ SẮP của chính nhóm đó:
        // lọc sau khi đếm sẽ cho ra chuỗi thua bị cắt ngang bởi một lệnh thắng của nhóm khác.
        var stylesSeen = closed.Select(t => t.Style)
            .Concat(openPositions.Select(p => p.Style))
            .Distinct()
            .ToList();

        static int StreakOf<T>(IEnumerable<T> orderedTrades, Func<T, TradeOutcome?> outcome)
        {
            var n = 0;
            foreach (var t in orderedTrades)
            {
                if (outcome(t) != TradeOutcome.Loss) break;
                n++;
            }
            return n;
        }

        var consecutiveLossesByStyle = stylesSeen.ToDictionary(
            st => st,
            st => StreakOf(ordered.Where(t => t.Style == st), t => t.Outcome));

        var consecutiveLossesTodayByStyle = stylesSeen.ToDictionary(
            st => st,
            st => StreakOf(orderedToday.Where(t => t.Style == st), t => t.Outcome));

        var lastLoss = ordered.FirstOrDefault(t => t.Outcome == TradeOutcome.Loss)?.ClosedAt;

        var recentForRisk = ordered.Take(Math.Max(1, lookback)).ToList();
        var scorecardIds = recentForRisk
            .Where(t => t.EntryScorecardId is not null)
            .Select(t => t.EntryScorecardId!.Value)
            .Distinct()
            .ToList();
        var disciplineMultipliers = scorecardIds.Count == 0
            ? new Dictionary<long, decimal>()
            : await _scorecards
                .Get(s => scorecardIds.Contains(s.Id))
                .AsNoTracking()
                .Select(s => new { s.Id, s.DisciplineMultiplier })
                .ToDictionaryAsync(s => s.Id, s => s.DisciplineMultiplier, ct);

        // Với lệnh do engine tạo, RiskPercent là size SAU kỷ luật. Dùng nó làm trung bình cho
        // OversizedGate tạo vòng phản hồi: gate giảm size hôm nay, trung bình ngày mai thấp hơn,
        // rồi gate lại giảm tiếp cho tới gần 0. Chia ngược hệ số đã lưu trên scorecard để lấy
        // đúng ý định trước kỷ luật. Lệnh tay không có scorecard vẫn dùng rủi ro thực tế.
        var averageRisk = recentForRisk
            .Where(t => t.RiskPercent is > 0m)
            .Select(t =>
            {
                var actual = t.RiskPercent!.Value;
                if (t.EntryScorecardId is not { } scorecardId
                    || !disciplineMultipliers.TryGetValue(scorecardId, out var multiplier)
                    || multiplier <= 0m)
                    return actual;

                return actual / multiplier;
            })
            .DefaultIfEmpty(0m)
            .Average();

        var balance = await _accounts
            .Get(a => a.Id == tradingAccountId)
            .AsNoTracking()
            .Select(a => (decimal?)a.CurrentBalance)
            .FirstOrDefaultAsync(ct) ?? 0m;

        var todayPnl = closed
            .Where(t => t.ClosedAt!.Value >= dayStart && t.ClosedAt.Value < dayStart.AddDays(1))
            .Sum(t => t.RealizedPnl ?? 0m);

        // Số DƯƠNG khi đang lỗ, để so thẳng với ngưỡng cấu hình vốn cũng là số dương.
        // Lãi trong ngày cho ra 0 chứ không cho ra số âm — "lỗ âm" là một khái niệm vô nghĩa
        // và sẽ khiến phép so sánh ở gate đọc ngược.
        var dailyLossPercent = balance > 0m && todayPnl < 0m
            ? Math.Abs(todayPnl) / balance * 100m
            : 0m;

        // Cùng quy ước dấu, áp riêng cho từng nhóm. Một nhóm lãi và nhóm kia lỗ thì con số gộp
        // che mất phần lỗ — đúng lúc phanh của nhóm đang lỗ cần nhìn thấy nó nhất.
        var dailyLossPercentByStyle = closed
            .Where(t => t.ClosedAt!.Value >= dayStart && t.ClosedAt.Value < dayStart.AddDays(1))
            .GroupBy(t => t.Style)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var pnl = g.Sum(t => t.RealizedPnl ?? 0m);
                    return balance > 0m && pnl < 0m ? Math.Abs(pnl) / balance * 100m : 0m;
                });

        var worstHours = closed
            .Where(t => t.OpenedAt is not null && t.Outcome == TradeOutcome.Loss)
            .GroupBy(t => t.OpenedAt!.Value.Hour)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)              // hoà thì lấy giờ nhỏ hơn, để kết quả tất định
            .Take(WorstHourCount)
            .Select(g => g.Key)
            .ToList();

        return new TraderStatistics(
            ConsecutiveLosses: streak,
            DailyLossPercent: dailyLossPercent,
            LastLossClosedAtUtc: lastLoss,
            AverageRiskRecent: averageRisk > 0m ? averageRisk : null,
            TradesToday: tradesToday,
            ClosedTradeCount: closed.Count,
            WorstHoursUtc: worstHours)
        {
            ConsecutiveLossesToday = streakToday,

            // RiskPercent null nghĩa là lệnh vào tay ngoài engine. Quy về 1R thay vì 0: coi một
            // vị thế đang chạy là "không có rủi ro" sẽ khiến gate tương quan cộng dồn ra 0 và
            // cho qua đúng lúc tài khoản đang cầm nhiều nhất.
            OpenPositions = openPositions
                .Select(p => new OpenPositionSnapshot(p.Symbol, p.Direction, p.RiskPercent ?? 1m, p.Style))
                .ToList(),

            TradesTodayByStyle = tradesTodayByStyle,
            DailyLossPercentByStyle = dailyLossPercentByStyle,
            ConsecutiveLossesByStyle = consecutiveLossesByStyle,
            ConsecutiveLossesTodayByStyle = consecutiveLossesTodayByStyle,
        };
    }
}
