using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Gắn Rule Engine + Behavior + TradingDay vào luồng lệnh.
///
/// Thứ tự quan trọng:
///   1) Rule Engine và Behavior chạy TRƯỚC khi cập nhật TradingDay, nên bảng TradingDay lúc này
///      vẫn phản ánh các lệnh TRƯỚC lệnh hiện tại → đúng quy ước "số lệnh trong ngày trước lệnh này"
///      mà MaxTradesPerDayRule/DailyLossLimitRule mong đợi.
///   2) Sau đó RecomputeAndSave cập nhật TradingDay để gồm cả lệnh hiện tại, phục vụ lệnh kế tiếp.
/// </summary>
public class TradeWorkflowService : ITradeWorkflowService
{
    private readonly IBaseRepository<Trade> _trades;
    private readonly IRuleEvaluationService _ruleEvaluation;
    private readonly IBehaviorAnalysisService _behaviorAnalysis;
    private readonly ITradingDayService _tradingDayService;

    public TradeWorkflowService(
        IBaseRepository<Trade> trades,
        IRuleEvaluationService ruleEvaluation,
        IBehaviorAnalysisService behaviorAnalysis,
        ITradingDayService tradingDayService)
    {
        _trades = trades;
        _ruleEvaluation = ruleEvaluation;
        _behaviorAnalysis = behaviorAnalysis;
        _tradingDayService = tradingDayService;
    }

    public async Task<TradeAnalysisResult> ProcessTradeAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await _trades.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy Trade {tradeId}.");

        var date = DateOnly.FromDateTime(trade.OpenedAt ?? trade.CreatedDate);

        // 1) Chấm rule (tính metrics + sinh Flag RuleViolation), đọc TradingDay = state trước lệnh này.
        var ruleFlags = await _ruleEvaluation.EvaluateTradeAsync(tradeId, cancellationToken);

        // 2) Phân tích hành vi (dựa lịch sử các lệnh trước).
        var behaviorFlags = await _behaviorAnalysis.AnalyzeTradeAsync(tradeId, cancellationToken);

        // 3) Cập nhật tổng hợp ngày để gồm cả lệnh hiện tại.
        await _tradingDayService.RecomputeAndSaveAsync(trade.TradingAccountId, date, cancellationToken);

        return new TradeAnalysisResult(ruleFlags, behaviorFlags);
    }
}
