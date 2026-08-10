using System.Text.Json;
using Microsoft.Extensions.Logging;
using MMW.Application.Backtest.Models;
using MMW.Application.MarketData.Models;
using MMW.Application.Services;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Backtest;

public interface IBacktestEngine
{
    Task<BacktestReport> RunAsync(BacktestRequest request, CancellationToken ct = default);
}

/// <summary>
/// Chạy lại tầng 1–3 trên dữ liệu lịch sử.
/// </summary>
/// <remarks>
/// Nguyên tắc chi phối: <b>kiểm thử lịch sử KHÔNG có mã riêng của nó</b>. Nó thay hai cổng —
/// <c>IClock</c> và <c>IMarketDataProvider</c> — rồi gọi đúng những service mà chạy thật gọi.
/// Bất kỳ dòng nào có dạng <c>if (isBacktest)</c> bên trong <c>MMW.Application.Trading</c> đều
/// là vi phạm FR-053.
///
/// Vì thế lớp này không chấm điểm gì cả. Nó chỉ đẩy đồng hồ, gọi <see cref="ISignalEvalService"/>,
/// rồi mô phỏng vòng đời vị thế trên nến kế tiếp.
/// </remarks>
public sealed class BacktestEngine : IBacktestEngine
{
    private const string EntryInterval = "15m";

    /// <summary>Độ trễ so với mốc đóng nến, khớp cron thật ở R-011.</summary>
    private static readonly TimeSpan EvalDelay = TimeSpan.FromMinutes(1);

    private readonly IKlineArchiveReader _archive;
    private readonly ISignalEvalService _signalEval;
    private readonly IDailyPlanService _dailyPlan;
    private readonly ITimeGuardService _timeGuard;
    private readonly BacktestClock _clock;
    private readonly IBaseRepository<BacktestRun> _runs;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly ITradeExecutionPlanner _executionPlanner;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BacktestEngine> _logger;

    public BacktestEngine(
        IKlineArchiveReader archive,
        ISignalEvalService signalEval,
        IDailyPlanService dailyPlan,
        ITimeGuardService timeGuard,
        BacktestClock clock,
        IBaseRepository<BacktestRun> runs,
        IBaseRepository<EngineSetting> settings,
        IBaseRepository<RiskSetting> riskSettings,
        ITradeExecutionPlanner executionPlanner,
        IUnitOfWork unitOfWork,
        ILogger<BacktestEngine> logger)
    {
        _archive = archive;
        _signalEval = signalEval;
        _dailyPlan = dailyPlan;
        _timeGuard = timeGuard;
        _clock = clock;
        _runs = runs;
        _settings = settings;
        _riskSettings = riskSettings;
        _executionPlanner = executionPlanner;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<BacktestReport> RunAsync(BacktestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var setting = request.SettingsOverride
            ?? await _settings.FirstOrDefaultAsync(s => s.TradingAccountId == request.TradingAccountId)
            ?? throw new InvalidOperationException(
                $"Tài khoản {request.TradingAccountId} chưa có cấu hình engine (EngineSetting).");
        var riskSetting = await _riskSettings.FirstOrDefaultAsync(
                              r => r.TradingAccountId == request.TradingAccountId)
                          ?? new RiskSetting { TradingAccountId = request.TradingAccountId };

        // Từ chối chạy khi kho khuyết, KHÔNG cảnh báo rồi chạy tiếp. Kết quả từ dữ liệu khuyết
        // trông hợp lệ nhưng sai — kiểu lỗi tệ nhất, vì không có gì để nghi ngờ.
        var gaps = new List<(DateTime From, DateTime To)>();
        foreach (var symbol in request.Symbols)
            gaps.AddRange(await _archive.FindGapsAsync(symbol, EntryInterval, request.FromUtc, request.ToUtc, ct));

        if (gaps.Count > 0)
        {
            var preview = string.Join(", ", gaps.Take(3).Select(g => $"{g.From:o}→{g.To:o}"));
            throw new InvalidOperationException(
                $"Kho nến thiếu {gaps.Count} khoảng trong [{request.FromUtc:o}, {request.ToUtc:o}): {preview}. " +
                "Nạp đủ dữ liệu rồi chạy lại — chạy trên dữ liệu khuyết cho ra kết quả trông hợp lệ nhưng sai.");
        }

        // Ba chuỗi nến và funding là dữ liệu bất biến trong suốt lần chạy. Nạp một lần rồi
        // cắt bằng tìm kiếm nhị phân; nếu truy vấn SQL ở mỗi nến, backtest 2 năm không thể
        // đạt cổng hiệu năng dù logic chấm điểm rất nhẹ.
        var preloadSymbols = request.Symbols
            .Append(DailyPlanService.AnchorSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var preloadIntervals = new[] { EntryInterval, setting.EntryTimeframe, setting.BiasTimeframe, "1d" }
            .Distinct(StringComparer.Ordinal)
            .ToList();
        await _archive.PreloadAsync(preloadSymbols, preloadIntervals, request.FromUtc, request.ToUtc, ct);
        await _timeGuard.PreloadAsync(
            request.TradingAccountId, request.Symbols, request.FromUtc, request.ToUtc, ct);

        var symbolsKey = string.Join(",", request.Symbols
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal));
        var comparableTrialNumber = await _runs.CountAsync(r =>
            r.Status == "Completed"
            && r.FromUtc == request.FromUtc
            && r.ToUtc == request.ToUtc
            && r.Symbols == symbolsKey) + 1;

        var run = new BacktestRun
        {
            Name = request.Name,
            StrategyVersion = setting.StrategyVersion,
            TelemetrySchemaVersion = request.CollectTelemetry
                ? BacktestTelemetryCollector.CurrentSchemaVersion
                : string.Empty,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            Symbols = symbolsKey,
            EngineSettingSnapshotJson = JsonSerializer.Serialize(new
            {
                setting.StrategyVersion,
                setting.MinScoreToEnter,
                setting.ScoreThresholdFull,
                setting.ScoreThresholdMax,
                setting.BacktestTakerFeePercent,
                setting.BacktestMakerFeePercent,
                setting.BacktestEntrySlippageBps,
                setting.BacktestStopSlippageBps,
                setting.BacktestLimitFillRequiresThrough,
                setting.LimitEntryExpiryBars,
                setting.TimeStopBars,
                setting.TimeStopMinR,
                setting.StopAtrMultipleMin,
                setting.StopAtrMultipleMax,
                setting.StopStructureBufferAtr,
                setting.MinStructuralRr,
                setting.RangeEdgePercent,
                setting.V3TriggerFreshBars,
                setting.V3MinImpulseVolumeMultiple,
                setting.V3PullbackVolumeMaxFraction,
                setting.V3RangeMinRelativeVolume,
                setting.V3MinNetRiskReward,
                setting.V3MaxCostToTargetPercent,
                setting.V3LockedNetRMin,
                setting.V6PatternLookbackBars,
                setting.V6PatternMinTouchesPerSide,
                setting.V6PatternContainmentPercent,
                setting.V6RectangleMinWidthAtr,
                setting.V6RectangleMaxWidthAtr,
                setting.V6RectangleMaxDriftAtr,
                setting.V6TriangleMaxEndWidthFraction,
                setting.V6RangeSweepLookbackBars,
                setting.V6RangeConfirmationMinRelativeVolume,
                setting.V6RangeStopBufferAtr,
                setting.V6BreakoutFreshBars,
                setting.V6BreakoutBufferAtr,
                setting.V6BreakoutMinRelativeVolume,
                setting.V6MinSetupQuality,
                setting.V6RangeRiskCap,
                setting.V6CompressionRiskCap,
                setting.V6TrendRiskCap,
                setting.V6RangeMinNetRiskReward,
                setting.V6BreakoutMinNetRiskReward,
            }),
            StartedAtUtc = request.FromUtc,
            Status = "Running",
            ComparableTrialNumber = comparableTrialNumber,
        };
        await _runs.AddAsync(run);
        await _unitOfWork.CommitAsync(ct);

        try
        {
        var trades = new List<SimulatedTradePosition>();
        var orders = new List<SimulatedTradePosition>();
        var open = new List<SimulatedTradePosition>();
        var latestCandles = new Dictionary<string, Candle>(StringComparer.OrdinalIgnoreCase);
        var decisionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var structuralRrs = new List<decimal>();
        var structuralRrVetoObservations = new List<decimal?>();
        var structuralEvaluatedCount = 0;
        var structuralVetoCount = 0;
        var unplannableStopCount = 0;
        var directionMarginMaterialBlocks = 0;
        using var telemetry = request.CollectTelemetry ? new BacktestTelemetryCollector() : null;
        var step = KlineArchiveService.IntervalSpan(EntryInterval);
        var lastPlanDate = default(DateOnly?);
        DailyPlan? currentPlan = null;

        for (var closeAt = Align(request.FromUtc, step) + step; closeAt <= request.ToUtc; closeAt += step)
        {
            // (a) Đẩy đồng hồ. Advance ném nếu bị lùi — dấu hiệu chắc chắn của lỗi nhìn trước.
            _clock.Advance(closeAt + EvalDelay);

            // (b) Sang ngày UTC mới thì lập kế hoạch, bằng ĐÚNG service mà chạy thật dùng.
            var today = DateOnly.FromDateTime(_clock.UtcNow);
            if (lastPlanDate != today)
            {
                if (request.PersistScorecards)
                    currentPlan = await _dailyPlan.GenerateAsync(request.TradingAccountId, today, ct);
                else if (_dailyPlan is DailyPlanService concretePlanService)
                    currentPlan = await concretePlanService.GenerateTransientAsync(request.TradingAccountId, today, ct);
                else
                    throw new InvalidOperationException(
                        "Backtest cần DailyPlanService để sinh kế hoạch transient mà không ghi vào bảng production.");
                lastPlanDate = today;
            }

            foreach (var symbol in request.Symbols)
            {
                // (f) Cập nhật vị thế đang mở TRƯỚC khi mở vị thế mới, để một cây nến không
                // vừa đóng lệnh cũ vừa mở lệnh mới trên cùng mức giá.
                var candle = (await _archive.GetRangeAsync(symbol, EntryInterval, closeAt - step, closeAt, ct))
                    .LastOrDefault();

                if (candle is not null)
                {
                    latestCandles[symbol] = candle;
                    foreach (var position in open.Where(p => p.Symbol == symbol).ToList())
                    {
                        if (!position.Advance(candle, setting)) continue;
                        open.Remove(position);
                        if (position.HasAnyFill && position.Outcome is not null) trades.Add(position);
                    }

                    await SettleFundingAsync(open, symbol, closeAt, step, candle, ct);
                }

                // (c–e) Chặn khung giờ → chấm điểm → tính kích thước, tất cả trong service thật.
                // Báo cáo sản xuất không cần ghi hàng chục nghìn phiếu chi tiết; làm vậy biến
                // một phép tính vài phút thành hơn một triệu INSERT. Test parity có thể bật
                // PersistScorecards để đọc lại toàn bộ chuỗi và so từng trường.
                if (currentPlan is null)
                    throw new InvalidOperationException($"Không sinh được kế hoạch cho ngày {today}.");

                var statistics = BuildStatistics(
                    trades, open, _clock.UtcNow, setting, riskSetting);
                var card = await _signalEval.EvaluateWithStatisticsAsync(
                    request.TradingAccountId,
                    symbol,
                    _clock.UtcNow,
                    statistics,
                    request.PersistScorecards,
                    ct,
                    // V2 giữ nguyên đường nạp cấu hình đã được parity chứng minh. V3 cần override
                    // để CLI chọn version thử nghiệm mà không ghi thay đổi vào cấu hình live.
                    settingsOverride: setting.StrategyVersion.UsesTriggerFirst()
                        ? setting
                        : null);
                telemetry?.ObserveDecision(card);
                var decisionKey = card.Outcome == ScorecardOutcome.Vetoed
                    ? $"Veto:{card.VetoReason?.ToString() ?? "Unknown"}"
                    : card.Outcome.ToString();
                decisionCounts[decisionKey] = decisionCounts.GetValueOrDefault(decisionKey) + 1;

                if (card.VetoReason == VetoReason.DirectionUnclear
                    && card.AvailableMaxPoints > 0
                    && (long)card.TotalScore * card.TotalMaxPoints
                    >= (long)setting.MinScoreToEnter * card.AvailableMaxPoints)
                {
                    directionMarginMaterialBlocks++;
                }

                // Đo TRƯỚC khi lọc theo Outcome. Chỉ nhìn các lệnh đã qua rào 1,6R sẽ lấy đúng
                // phần đuôi của phân phối rồi dùng nó để biện minh cho chính cái rào đã cắt mẫu.
                // Dòng structural_room xác nhận tiêu chí thật sự đã được chạy; các veto sớm như
                // NotAtRangeEdge chưa dựng cấu trúc nên không được trộn vào mẫu số.
                if (card.Lines.Any(l => l.CriterionKey == "technical.structural_room"))
                {
                    structuralEvaluatedCount++;
                    if (card.RiskReward is { } rr) structuralRrs.Add(rr);

                    if (card.VetoReason == VetoReason.InsufficientRoom)
                    {
                        structuralVetoCount++;
                        structuralRrVetoObservations.Add(card.RiskReward);
                        if (card.RiskReward is null) unplannableStopCount++;
                    }
                }

                if (card.Outcome != ScorecardOutcome.Entered || card.FinalSizeR <= 0m) continue;
                if (card.Direction is not { } direction) continue;

                var execution = _executionPlanner.Plan(card, currentPlan, setting);
                var order = SimulatedTradePosition.Open(
                    symbol,
                    direction,
                    _clock.UtcNow,
                    card.FinalSizeR,
                    card.EffectiveDayRegime ?? currentPlan.DayRegime,
                    execution,
                    setting,
                    card.BaseSizeR
                    * card.DayRiskMultiplier
                    * card.AiMultiplier
                    * card.DataMultiplier);
                orders.Add(order);
                open.Add(order);
                telemetry?.TrackOrder(order, card, execution);
            }
        }

        // Không loại lệnh còn mở khỏi mẫu: đóng theo giá nến cuối cùng để tránh survivorship
        // bias và để số lệnh đã vào khớp đúng số lệnh trong báo cáo.
        foreach (var position in open.ToList())
        {
            if (!latestCandles.TryGetValue(position.Symbol, out var lastCandle)) continue;
            position.CloseAtMarket(lastCandle, setting);
            open.Remove(position);
            if (position.HasAnyFill && position.Outcome is not null) trades.Add(position);
        }

        var structuralDistribution = BacktestStatistics.StructuralRr(
            structuralEvaluatedCount,
            structuralRrs,
            structuralVetoCount,
            unplannableStopCount);
        var telemetryReport = telemetry?.Build(trades);
        var report = Summarise(
            run.Id, trades, orders, setting, missingCandles: 0, structuralDistribution,
            comparableTrialNumber, directionMarginMaterialBlocks, telemetryReport);

        var modeSummary = string.Join(", ", trades
            .GroupBy(t => t.Mode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()} ({g.Average(t => t.RMultiple):N4}R)"));
        var yearSummary = string.Join(", ", trades
            .GroupBy(t => t.OpenedAtUtc.Year)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()} ({g.Average(t => t.RMultiple):N4}R)"));
        var decisionSummary = string.Join(", ", decisionCounts
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        _logger.LogInformation(
            "Backtest diagnostics. trial={Trial} modes=[{Modes}] years=[{Years}] decisions=[{Decisions}] " +
            "structuralRr=[n={Observed}/{Evaluated}, p50={P50}, p75={P75}, p90={P90}]",
            comparableTrialNumber, modeSummary, yearSummary, decisionSummary,
            structuralDistribution.ObservedCount, structuralDistribution.EvaluatedCount,
            structuralDistribution.Median, structuralDistribution.P75, structuralDistribution.P90);

        run.Status = "Completed";
        run.CompletedAtUtc = _clock.UtcNow;
        run.TradeCount = report.TradeCount;
        run.WinRate = report.WinRate;
        run.WinRateCiLow = report.WinRate95?.Lower ?? 0m;
        run.WinRateCiHigh = report.WinRate95?.Upper ?? 0m;
        run.ExpectancyR = report.ExpectancyR;
        run.ExpectancyRCiLow = report.ExpectancyR95?.Lower ?? 0m;
        run.ExpectancyRCiHigh = report.ExpectancyR95?.Upper ?? 0m;
        run.MaxDrawdownPercent = report.MaxDrawdownPercent;
        run.LongestLossStreak = report.LongestLossStreak;
        run.TotalFees = report.TotalFees;
        run.TotalSlippage = report.TotalSlippage;
        run.TotalFeeR = report.TotalFeeR;
        run.TotalFundingR = report.TotalFundingR;
        run.TotalSlippageR = report.TotalSlippageR;
        run.GrossExpectancyR = report.GrossExpectancyR;
        run.DecisionFingerprint = report.Telemetry?.DecisionFingerprint ?? string.Empty;
        run.TradeFingerprint = report.Telemetry?.TradeFingerprint ?? string.Empty;
        run.DiagnosticsJson = report.Telemetry is null
            ? string.Empty
            : JsonSerializer.Serialize(report.Telemetry);
        run.BreakdownByHourJson = JsonSerializer.Serialize(report.ByHourUtc);
        run.BreakdownByRegimeJson = JsonSerializer.Serialize(report.ByRegime);
        run.BreakdownByModeJson = JsonSerializer.Serialize(report.ByMode);
        run.BreakdownByExitReasonJson = JsonSerializer.Serialize(report.ByExitReason);
        run.StructuralRrDistributionJson = JsonSerializer.Serialize(report.StructuralRr);
        run.StructuralRrVetoObservationsJson = JsonSerializer.Serialize(structuralRrVetoObservations);
        run.DirectionMarginMaterialBlocks = report.DirectionMarginMaterialBlocks;
        run.Limitations = string.Join(Environment.NewLine, report.Limitations);

        _runs.Update(run);
        await _unitOfWork.CommitAsync(ct);

        _logger.LogInformation(
            "Kiểm thử lịch sử xong. runId={RunId} trades={Trades} winRate={WinRate} expectancyR={Expectancy}",
            run.Id, report.TradeCount, report.WinRate, report.ExpectancyR);

        return report with { RunId = run.Id };
        }
        catch (Exception ex)
        {
            // Một lần chạy đã có row audit thì không được mắc kẹt ở Running khi tiến trình lỗi.
            // Giữ exception gốc để CLI thất bại rõ ràng, nhưng chốt trạng thái trước khi ném lại.
            run.Status = "Failed";
            run.CompletedAtUtc = _clock.UtcNow;
            run.Limitations = string.Join(
                Environment.NewLine,
                new[] { run.Limitations, $"Run thất bại: {ex.Message}" }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            _runs.Update(run);
            await _unitOfWork.CommitAsync(CancellationToken.None);
            _logger.LogError(ex, "Kiểm thử lịch sử thất bại. runId={RunId}", run.Id);
            throw;
        }
    }

    /// <summary>
    /// Thanh toán phí vốn cho các vị thế còn mở khi cây nến vừa đóng đi qua một mốc funding.
    /// </summary>
    /// <remarks>
    /// Gọi SAU khi đã xử lý stop/target của cây nến, và đó là điểm mấu chốt về tính đúng đắn:
    /// mốc thanh toán nằm ở BIÊN nến (00:00/08:00/16:00 UTC đều rơi đúng lưới 15 phút), nên một
    /// vị thế sống sót trọn cây nến thì thật sự còn mở tại mốc đó và phải trả. Vị thế bị dừng
    /// trong cây nến đã thoát trước mốc — không phải trả. Đây là kết luận CHÍNH XÁC, không phải
    /// xấp xỉ, và nó chỉ đúng nhờ thứ tự gọi này.
    ///
    /// Vị thế mở tại cây nến này cũng không trả: nó vào lệnh tại <c>closeAt + EvalDelay</c>,
    /// tức là sau mốc thanh toán.
    ///
    /// Giả định: <paramref name="step"/> không vượt 8 giờ, nên mỗi cây nến chứa nhiều nhất một mốc.
    /// </remarks>
    private async Task SettleFundingAsync(
        List<SimulatedTradePosition> open,
        string symbol,
        DateTime closeAt,
        TimeSpan step,
        Candle candle,
        CancellationToken ct)
    {
        if (open.Count == 0) return;

        var settlement = await _archive.GetFundingAtAsync(symbol, closeAt, ct);
        if (settlement is null || settlement.FundingTimeUtc <= closeAt - step) return;

        // Kho lưu giá đánh dấu tại mốc; thiếu thì lùi về giá đóng cửa của chính cây nến đó.
        var markPrice = settlement.MarkPrice is > 0m ? settlement.MarkPrice.Value : candle.Close;

        foreach (var position in open.Where(p => p.Symbol == symbol))
            position.SettleFunding(settlement.FundingRate, markPrice);
    }

    private static TraderStatistics BuildStatistics(
        IReadOnlyCollection<SimulatedTradePosition> closed,
        IReadOnlyCollection<SimulatedTradePosition> open,
        DateTime utcNow,
        EngineSetting setting,
        RiskSetting riskSetting)
    {
        var ordered = closed.OrderByDescending(t => t.ClosedAtUtc).ToList();
        var streak = 0;
        foreach (var trade in ordered)
        {
            if (trade.Outcome != TradeOutcome.Loss) break;
            streak++;
        }

        var dayStart = utcNow.Date;
        var dayEnd = dayStart.AddDays(1);
        var streakToday = 0;
        foreach (var trade in ordered.Where(t => t.ClosedAtUtc >= dayStart && t.ClosedAtUtc < dayEnd))
        {
            if (trade.Outcome != TradeOutcome.Loss) break;
            streakToday++;
        }
        var todayNetR = closed
            .Where(t => t.ClosedAtUtc >= dayStart && t.ClosedAtUtc < dayEnd)
            .Sum(t => t.RealizedR);
        var dailyLossPercent = todayNetR < 0m
            ? Math.Abs(todayNetR) * riskSetting.MaxRiskPerTradePercent
            : 0m;

        var averageRisk = ordered
            .Take(Math.Max(1, setting.OversizeLookbackTrades))
            .Select(t => t.PlannedSizeRBeforeDiscipline * riskSetting.MaxRiskPerTradePercent)
            .DefaultIfEmpty(0m)
            .Average();

        var worstHours = closed
            .Where(t => t.Outcome == TradeOutcome.Loss)
            .GroupBy(t => t.OpenedAtUtc.Hour)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(2)
            .Select(g => g.Key)
            .ToList();

        return new TraderStatistics(
            ConsecutiveLosses: streak,
            DailyLossPercent: dailyLossPercent,
            LastLossClosedAtUtc: ordered.FirstOrDefault(t => t.Outcome == TradeOutcome.Loss)?.ClosedAtUtc,
            AverageRiskRecent: averageRisk > 0m ? averageRisk : null,
            TradesToday: closed.Concat(open.Where(t => t.HasAnyFill))
                .Count(t => t.OpenedAtUtc >= dayStart && t.OpenedAtUtc < dayEnd),
            ClosedTradeCount: closed.Count,
            WorstHoursUtc: worstHours)
        {
            ConsecutiveLossesToday = streakToday,

            // Vị thế mô phỏng đang chạy, nhìn bằng đúng con mắt mà chạy thật nhìn bảng Trade.
            // Không có dòng này thì `discipline.open_position` luôn thấy danh sách rỗng và kiểm
            // thử lịch sử sẽ báo cáo một chiến lược mà gate của nó chưa từng được chạy.
            OpenPositions = open
                // Lệnh chờ phải chặn setup trùng cùng mã, nhưng chưa có exposure tương quan.
                .Select(p => new OpenPositionSnapshot(
                    p.Symbol, p.Direction, p.HasAnyFill ? p.SizeR : 0m))
                .ToList(),
        };
    }

    private static BacktestReport Summarise(
        long runId,
        List<SimulatedTradePosition> trades,
        List<SimulatedTradePosition> orders,
        EngineSetting setting,
        int missingCandles,
        StructuralRrDistribution structuralRr,
        int comparableTrialNumber,
        int directionMarginMaterialBlocks,
        BacktestTelemetryReport? telemetry)
    {
        var limitations = BacktestLimitations.Build(
            setting.BacktestTakerFeePercent,
            setting.BacktestEntrySlippageBps,
            setting.BacktestStopSlippageBps,
            missingCandles,
            trades.Sum(t => t.FundingSettlements),
            setting.BacktestMakerFeePercent,
            setting.BacktestLimitFillRequiresThrough,
            orders.Sum(t => t.LimitTranchesOffered),
            orders.Sum(t => t.LimitTranchesFilled),
            orders.Sum(t => t.LimitTranchesExpired));

        if (trades.Count == 0)
        {
            return new BacktestReport(runId, 0, 0m, 0m, 0m, 0, 0m, 0m,
                new Dictionary<int, HourStats>(), new Dictionary<DayRegime, RegimeStats>(), limitations,
                WinRate95: BacktestStatistics.WinRate95(0, 0),
                ExpectancyR95: BacktestStatistics.Mean95(Array.Empty<decimal>()),
                ByMode: new Dictionary<string, TradeGroupStats>(),
                ByExitReason: new Dictionary<BacktestExitReason, TradeGroupStats>(),
                StructuralRr: structuralRr,
                ComparableTrialNumber: comparableTrialNumber,
                DirectionMarginMaterialBlocks: directionMarginMaterialBlocks,
                StrategyVersion: setting.StrategyVersion,
                GrossExpectancyR: telemetry?.GrossExpectancyR ?? 0m,
                Telemetry: telemetry);
        }

        var wins = trades.Count(t => t.Outcome == TradeOutcome.Win);
        var winRate = (decimal)wins / trades.Count * 100m;
        var expectancy = trades.Average(t => t.RMultiple);

        var streak = 0;
        var longestStreak = 0;
        var equity = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;

        foreach (var t in trades.OrderBy(t => t.ClosedAtUtc))
        {
            streak = t.Outcome == TradeOutcome.Loss ? streak + 1 : 0;
            longestStreak = Math.Max(longestStreak, streak);

            // Đường vốn phản ánh size thật; expectancy phía trên dùng R-multiple chuẩn hoá.
            equity += t.RealizedR;
            peak = Math.Max(peak, equity);
            maxDrawdown = Math.Max(maxDrawdown, peak - equity);
        }

        var byHour = trades
            .GroupBy(t => t.OpenedAtUtc.Hour)
            .ToDictionary(g => g.Key, g => new HourStats(
                g.Count(),
                (decimal)g.Count(t => t.Outcome == TradeOutcome.Win) / g.Count() * 100m,
                g.Average(t => t.RMultiple)));

        var byRegime = trades
            .GroupBy(t => t.Regime)
            .ToDictionary(g => g.Key, g => new RegimeStats(
                g.Count(),
                (decimal)g.Count(t => t.Outcome == TradeOutcome.Win) / g.Count() * 100m,
                g.Average(t => t.RMultiple)));

        var byMode = trades
            .GroupBy(t => t.Mode)
            .ToDictionary(
                g => g.Key,
                g => BacktestStatistics.Group(g.ToList()),
                StringComparer.Ordinal);

        var byExitReason = trades
            .Where(t => t.ExitReason is not null)
            .GroupBy(t => t.ExitReason!.Value)
            .ToDictionary(g => g.Key, g => BacktestStatistics.Group(g.ToList()));

        var values = trades.Select(t => t.RMultiple).ToList();

        return new BacktestReport(
            runId,
            trades.Count,
            winRate,
            expectancy,
            maxDrawdown,
            longestStreak,
            trades.Sum(t => t.FeePercent),
            trades.Sum(t => t.TotalSlippage),
            byHour,
            byRegime,
            limitations,
            TotalFeeR: trades.Sum(t => NormaliseCost(t.FeeR, t.FilledRiskBudgetR)),
            TotalFundingR: trades.Sum(t => NormaliseCost(t.FundingR, t.FilledRiskBudgetR)),
            TotalSlippageR: trades.Sum(t => NormaliseCost(t.SlippageR, t.FilledRiskBudgetR)),
            MakerFeeR: trades.Sum(t => NormaliseCost(t.MakerFeeR, t.FilledRiskBudgetR)),
            TakerFeeR: trades.Sum(t => NormaliseCost(t.TakerFeeR, t.FilledRiskBudgetR)),
            LimitTranchesOffered: orders.Sum(t => t.LimitTranchesOffered),
            LimitTranchesFilled: orders.Sum(t => t.LimitTranchesFilled),
            LimitTranchesExpired: orders.Sum(t => t.LimitTranchesExpired),
            WinRate95: BacktestStatistics.WinRate95(wins, trades.Count),
            ExpectancyR95: BacktestStatistics.Mean95(values),
            ByMode: byMode,
            ByExitReason: byExitReason,
            StructuralRr: structuralRr,
            ComparableTrialNumber: comparableTrialNumber,
            DirectionMarginMaterialBlocks: directionMarginMaterialBlocks,
            StrategyVersion: setting.StrategyVersion,
            GrossExpectancyR: telemetry?.GrossExpectancyR ?? 0m,
            Telemetry: telemetry);
    }

    private static decimal NormaliseCost(decimal costRiskUnits, decimal filledRiskBudgetR) =>
        filledRiskBudgetR > 0m ? costRiskUnits / filledRiskBudgetR : 0m;

    private static DateTime Align(DateTime value, TimeSpan step) =>
        new(value.Ticks - value.Ticks % step.Ticks, DateTimeKind.Utc);

}
