using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Application.RuleEngine;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Điều phối Rule Engine với dữ liệu thật: nạp lệnh + cấu hình + tổng hợp ngày,
/// tính chỉ số rủi ro, chạy engine và lưu Flag.
/// </summary>
public class RuleEvaluationService : IRuleEvaluationService
{
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IBaseRepository<TradingDay> _tradingDays;
    private readonly IBaseRepository<Flag> _flags;
    private readonly ITradeMetricsCalculator _calculator;
    private readonly IRuleEngine _engine;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RuleEvaluationService> _logger;

    public RuleEvaluationService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IBaseRepository<TradingDay> tradingDays,
        IBaseRepository<Flag> flags,
        ITradeMetricsCalculator calculator,
        IRuleEngine engine,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        ILogger<RuleEvaluationService> logger)
    {
        _trades = trades;
        _accounts = accounts;
        _riskSettings = riskSettings;
        _tradingDays = tradingDays;
        _flags = flags;
        _calculator = calculator;
        _engine = engine;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Flag>> EvaluateTradeAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await _trades.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy Trade {tradeId}.");

        var account = await _accounts.FindAsync(trade.TradingAccountId)
            ?? throw new InvalidOperationException($"Không tìm thấy TradingAccount {trade.TradingAccountId}.");

        // Cấu hình rủi ro — nếu tài khoản chưa cấu hình thì dùng mặc định.
        var settings = await _riskSettings.FirstOrDefaultAsync(s => s.TradingAccountId == account.Id)
            ?? new RiskSetting { TradingAccountId = account.Id };

        // Tổng hợp ngày của lệnh (theo thời điểm vào lệnh).
        var tradeDate = DateOnly.FromDateTime(trade.OpenedAt ?? trade.CreatedDate);
        var day = await _tradingDays.FirstOrDefaultAsync(
            d => d.TradingAccountId == account.Id && d.Date == tradeDate);

        // 1) Tính lại chỉ số rủi ro (ghi vào entity đang được tracking).
        _calculator.Compute(trade, account.CurrentBalance);

        // 2) Chạy Rule Engine.
        var context = new RuleEvaluationContext
        {
            Trade = trade,
            Settings = settings,
            AccountEquity = account.CurrentBalance,
            Day = day,
        };
        var violations = _engine.Evaluate(context);

        // 3) Xoá Flag RuleViolation cũ của lệnh (idempotent), rồi thêm cờ mới.
        // Dùng tracked query (Queryable) thay AsNoTracking để EF dùng identity map,
        // tránh lỗi duplicate-tracking khi DbContext đã track các Flag đó từ trước.
        var existing = await _flags.Queryable
            .Where(f => f.TradeId == trade.Id && f.Category == FlagCategory.RuleViolation)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
            _flags.RemoveRange(existing);

        var newFlags = violations.Select(v => new Flag
        {
            TradingAccountId = account.Id,
            TradeId = trade.Id,
            Category = FlagCategory.RuleViolation,
            Type = v.Type,
            Severity = v.Severity,
            Message = v.Message,
            DetailJson = v.DetailJson,
            DetectedAt = DateTime.UtcNow,
        }).ToList();

        if (newFlags.Count > 0)
            await _flags.AddRangeAsync(newFlags);

        await _unitOfWork.CommitAsync(cancellationToken);

        await NotifyRuleViolationsAsync(trade, newFlags, cancellationToken);

        return newFlags;
    }

    private async Task NotifyRuleViolationsAsync(Trade trade, IReadOnlyList<Flag> flags, CancellationToken cancellationToken)
    {
        foreach (var flag in flags.Where(f => f.Severity >= FlagSeverity.Warning))
        {
            try
            {
                await _notifications.PublishAsync(new NotificationCreateModel
                {
                    Type = NotificationType.TradeRiskWarning,
                    Severity = flag.Severity == FlagSeverity.Critical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    Title = $"Cảnh báo rủi ro {trade.Symbol}",
                    Message = flag.Message,
                    Source = "rule_engine",
                    SourceKey = $"{trade.Id}:{flag.Type}",
                    RelatedSymbol = trade.Symbol,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                // Notification không được làm fail workflow chấm rule.
                _logger.LogWarning(ex, "Failed to publish rule violation notification for trade {TradeId} and flag {FlagType}", trade.Id, flag.Type);
            }
        }
    }
}
