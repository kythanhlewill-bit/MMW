using MMW.Application.Behavior;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Điều phối BehaviorAnalyzer với dữ liệu thật: nạp lệnh + cấu hình + lịch sử,
/// chạy các detector và lưu Flag hành vi.
/// </summary>
public class BehaviorAnalysisService : IBehaviorAnalysisService
{
    /// <summary>Số lệnh lịch sử gần nhất nạp để phân tích (đủ cho streak + trung bình size).</summary>
    private const int HistoryLimit = 50;

    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IBaseRepository<Flag> _flags;
    private readonly IBehaviorAnalyzer _analyzer;
    private readonly IUnitOfWork _unitOfWork;

    public BehaviorAnalysisService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IBaseRepository<Flag> flags,
        IBehaviorAnalyzer analyzer,
        IUnitOfWork unitOfWork)
    {
        _trades = trades;
        _accounts = accounts;
        _riskSettings = riskSettings;
        _flags = flags;
        _analyzer = analyzer;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<Flag>> AnalyzeTradeAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await _trades.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy Trade {tradeId}.");

        var account = await _accounts.FindAsync(trade.TradingAccountId)
            ?? throw new InvalidOperationException($"Không tìm thấy TradingAccount {trade.TradingAccountId}.");

        var settings = await _riskSettings.FirstOrDefaultAsync(s => s.TradingAccountId == account.Id)
            ?? new RiskSetting { TradingAccountId = account.Id };

        // Lịch sử: các lệnh khác cùng tài khoản, xảy ra trước lệnh hiện tại.
        var currentTimeline = BehaviorContext.Timeline(trade);
        var history = (await _trades.FindListAsync(
                t => t.TradingAccountId == account.Id && t.Id != trade.Id))
            .Where(t => BehaviorContext.Timeline(t) <= currentTimeline)
            .OrderBy(t => BehaviorContext.Timeline(t))
            .ThenBy(t => t.Id)
            .TakeLast(HistoryLimit)
            .ToList();

        var context = new BehaviorContext
        {
            Trade = trade,
            Settings = settings,
            History = history,
        };

        var signals = _analyzer.Analyze(context);

        // Idempotent: xoá Flag Behavior cũ của lệnh, thêm cờ mới.
        var existing = await _flags.FindListAsync(
            f => f.TradeId == trade.Id && f.Category == FlagCategory.Behavior);
        if (existing.Count > 0)
            _flags.RemoveRange(existing);

        var newFlags = signals.Select(s => new Flag
        {
            TradingAccountId = account.Id,
            TradeId = trade.Id,
            Category = FlagCategory.Behavior,
            Type = s.Type,
            Severity = s.Severity,
            Message = s.Message,
            DetailJson = s.DetailJson,
            DetectedAt = DateTime.UtcNow,
        }).ToList();

        if (newFlags.Count > 0)
            await _flags.AddRangeAsync(newFlags);

        await _unitOfWork.CommitAsync(cancellationToken);

        return newFlags;
    }
}
