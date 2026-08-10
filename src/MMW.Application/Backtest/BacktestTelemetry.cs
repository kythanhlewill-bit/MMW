using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MMW.Application.Backtest.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;

namespace MMW.Application.Backtest;

public sealed record TelemetryBucketStats(
    int Bucket,
    decimal Minimum,
    decimal Maximum,
    TradeGroupStats Stats);

public sealed record ExcursionTelemetry(
    decimal AverageMfeR,
    decimal AverageMaeR,
    decimal WinnerAverageMfeR,
    decimal WinnerAverageMaeR,
    decimal LoserAverageMfeR,
    decimal LoserAverageMaeR,
    decimal AverageBarsToMfe,
    decimal AverageBarsToMae);

public enum EntryFillState
{
    NoFills = 0,
    NoLimitPlanned = 1,
    MarketOnly = 2,
    MarketPlusLimit = 3,
    LimitOnly = 4,
}

public sealed record EntryFillGroupStats(
    TradeGroupStats Trades,
    decimal GrossExpectancyR,
    decimal AverageFrictionR,
    decimal AverageFilledRiskBudgetFraction,
    decimal AverageFilledLimitRiskWeight);

public sealed record EntryFillTelemetry(
    int MarketTranchesOffered,
    int MarketTranchesFilled,
    int LimitTranchesOffered,
    int LimitTranchesFilled,
    int LimitTranchesExpired,
    IReadOnlyDictionary<string, EntryFillGroupStats> ByFillState,
    IReadOnlyDictionary<string, EntryFillGroupStats> BySetupAndFillState)
{
    public decimal MarketFillRatePercent => MarketTranchesOffered == 0
        ? 0m
        : (decimal)MarketTranchesFilled / MarketTranchesOffered * 100m;

    public decimal LimitFillRatePercent => LimitTranchesOffered == 0
        ? 0m
        : (decimal)LimitTranchesFilled / LimitTranchesOffered * 100m;
}

/// <summary>
/// Phân loại theo fill THỰC TẾ, không theo loại lệnh được dự định. V3 luôn có market tranche,
/// nên so sánh hữu ích là MarketOnly với MarketPlusLimit; đó vẫn là attribution quan sát và
/// không được đọc như counterfactual của cùng một đường giá.
/// </summary>
public static class EntryFillClassifier
{
    public static EntryFillState Classify(SimulatedTradePosition trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        var limitPlanned = trade.Entries.Any(x => x.IsLimit);
        var marketFilled = trade.Entries.Any(x => !x.IsLimit && x.IsFilled);
        var limitFilled = trade.Entries.Any(x => x.IsLimit && x.IsFilled);

        if (!marketFilled && !limitFilled) return EntryFillState.NoFills;
        if (!limitPlanned) return EntryFillState.NoLimitPlanned;
        if (marketFilled && limitFilled) return EntryFillState.MarketPlusLimit;
        if (marketFilled) return EntryFillState.MarketOnly;
        return EntryFillState.LimitOnly;
    }
}

/// <summary>
/// Một dòng cho một lệnh, tách rõ đại lượng BIẾT TRƯỚC khi vào lệnh khỏi đại lượng chỉ biết sau
/// khi lệnh đã đóng.
/// </summary>
/// <remarks>
/// Đây là lý do bản ghi này tồn tại. Decile theo <c>ActualCostR</c> trộn phí vốn — thứ tích luỹ
/// theo thời gian giữ lệnh — vào cùng một con số với phí giao dịch và trượt giá vốn tính được từ
/// plan. Một bộ lọc dựng trên <c>ActualCostR</c> vì vậy nhìn thấy tương lai và không thể chạy
/// thật. Chỉ các trường trong nhóm "biết trước" mới được phép xuất hiện trong một công thức vào lệnh.
/// </remarks>
public sealed record TelemetryTradeRow(
    // ── Biết TRƯỚC khi vào lệnh ─────────────────────────────────────────
    string Symbol,
    DateTime OrderPlacedAtUtc,
    string Direction,
    string Setup,
    string Trigger,
    string Mode,
    string Regime,
    int Score,
    decimal? ExpectedCostR,
    decimal? NetRiskReward,
    decimal? RiskReward,
    decimal? StopDistanceBps,
    decimal PlannedSizeR,
    /// <summary>Điểm từng tiêu chí, dạng <c>key=points;…</c> đã sắp xếp theo key.</summary>
    string CriterionPoints,
    // ── Chỉ biết SAU khi lệnh đóng ──────────────────────────────────────
    string FillState,
    decimal RMultiple,
    decimal FeeR,
    decimal FundingR,
    decimal SlippageR,
    decimal FilledRiskBudgetR,
    decimal ActualCostR,
    int FundingSettlements,
    int BarsHeld,
    decimal MfeR,
    decimal MaeR,
    string ExitReason,
    string Outcome);

/// <summary>Aggregate P0: đủ để quy nguyên nhân nhưng không INSERT từng scorecard của full run.</summary>
public sealed record BacktestTelemetryReport(
    string SchemaVersion,
    int DecisionCount,
    int EnteredDecisionCount,
    int TrackedOrderCount,
    string DecisionFingerprint,
    string TradeFingerprint,
    decimal GrossExpectancyR,
    decimal AverageFrictionR,
    IReadOnlyDictionary<string, int> DecisionBreakdown,
    IReadOnlyDictionary<string, TradeGroupStats> ByDayOfWeek,
    IReadOnlyDictionary<string, TradeGroupStats> ByScoreBand,
    IReadOnlyDictionary<string, TradeGroupStats> BySetup,
    IReadOnlyDictionary<string, TradeGroupStats> ByTrigger,
    IReadOnlyDictionary<string, TradeGroupStats> ByCriterionPoint,
    IReadOnlyList<TelemetryBucketStats> CostDeciles,
    IReadOnlyList<TelemetryBucketStats> StopDistanceDeciles,
    ExcursionTelemetry Excursions,
    EntryFillTelemetry? EntryFills = null,
    int DistinctCandidateEventCount = 0,
    int DistinctConfirmedEventCount = 0,
    int DistinctEnteredEventCount = 0,
    // Không serialize vào DiagnosticsJson: full run V2 có gần 5.000 lệnh và cột này chỉ phục vụ
    // phân tích ngoại tuyến qua `--dump`, không phải bản ghi thường trực của lần chạy.
    [property: JsonIgnore] IReadOnlyList<TelemetryTradeRow>? TradeRows = null);

/// <summary>
/// Observer một chiều: chỉ đọc card/plan/trade. Không trả dữ liệu nào về vòng quyết định, vì vậy
/// compiler-level data flow bảo đảm telemetry không thể thay đổi lệnh.
/// </summary>
internal sealed class BacktestTelemetryCollector : IDisposable
{
    public const string CurrentSchemaVersion = "P0-D0.3";

    private sealed record EntryMetadata(
        int Score,
        string Setup,
        string Trigger,
        string Mode,
        decimal? StopDistanceBps,
        IReadOnlyDictionary<string, int> CriterionPoints,
        decimal? ExpectedCostR,
        decimal? NetRiskReward,
        decimal? RiskReward,
        string Direction);

    private sealed record TradeObservation(
        SimulatedTradePosition Trade,
        EntryMetadata Metadata,
        decimal ActualCostR);

    private readonly IncrementalHash _decisionHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Dictionary<string, int> _decisionBreakdown = new(StringComparer.Ordinal);
    private readonly Dictionary<SimulatedTradePosition, EntryMetadata> _metadata =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _candidateEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _confirmedEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _enteredEvents = new(StringComparer.Ordinal);
    private int _decisionCount;
    private int _enteredDecisionCount;
    private bool _built;

    public void ObserveDecision(EntryScorecard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _decisionCount++;
        if (card.Outcome == Domain.Enums.ScorecardOutcome.Entered) _enteredDecisionCount++;

        Count($"Outcome:{card.Outcome}");
        if (card.VetoReason is { } veto) Count($"Veto:{veto}");
        Count($"Trigger:{card.TriggerState}");
        Count($"SetupStage:{card.SetupStage}");
        Count($"SetupType:{card.SetupType}");
        Count($"SetupQuality:{ScoreBand(card.SetupQualityScore)}");
        if (!string.IsNullOrWhiteSpace(card.SetupEventId))
        {
            if (card.SetupStage >= Domain.Enums.SetupFunnelStage.StructureCandidate)
                _candidateEvents.Add(card.SetupEventId);
            if (card.SetupStage == Domain.Enums.SetupFunnelStage.Confirmed)
                _confirmedEvents.Add(card.SetupEventId);
            if (card.Outcome == Domain.Enums.ScorecardOutcome.Entered)
                _enteredEvents.Add(card.SetupEventId);
        }
        if (card.TriggerState is Domain.Enums.SetupTriggerState.Confirmed
            or Domain.Enums.SetupTriggerState.CostRejected)
        {
            Count($"TriggerConfirmed:Outcome:{card.Outcome}");
            Count($"TriggerConfirmed:Score:{ScoreBand(card.TotalScore)}");
            if (card.VetoReason is { } confirmedVeto)
                Count($"TriggerConfirmed:Veto:{confirmedVeto}");
        }
        if (card.TriggerState == Domain.Enums.SetupTriggerState.CostRejected)
        {
            Count($"CostRejected:NetRR:{NumericBand(card.NetRiskReward, 0.25m)}");
            Count($"CostRejected:ExpectedCostR:{NumericBand(card.ExpectedCostR, 0.05m)}");
            Count($"CostRejected:StopBps:{NumericBand(card.StopDistanceBps, 25m)}");
        }

        var canonical = new StringBuilder(320)
            .Append(card.Symbol).Append('|')
            .Append(card.CandleCloseTimeUtc.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(card.StrategyVersion).Append('|')
            .Append(card.Direction).Append('|')
            .Append(card.TotalScore).Append('|')
            .Append(card.Outcome).Append('|')
            .Append(card.VetoReason).Append('|')
            .Append(card.SetupType).Append('|')
            .Append(card.TriggerState).Append('|')
            .Append(card.SetupStage).Append('|')
            .Append(card.SetupEventId).Append('|')
            .Append(card.SetupQualityScore).Append('|')
            .Append(D(card.SetupSizeMultiplier)).Append('|')
            .Append(D(card.FinalSizeR)).Append('|')
            .Append(D(card.SuggestedEntry)).Append('|')
            .Append(D(card.SuggestedStopLoss)).Append('|')
            .Append(D(card.SuggestedTakeProfit)).Append('|');

        foreach (var line in card.Lines.OrderBy(l => l.CriterionKey, StringComparer.Ordinal))
            canonical.Append(line.CriterionKey).Append('=').Append(line.AwardedPoints)
                .Append(':').Append(line.StateCode).Append(';');

        Append(_decisionHash, canonical.ToString());
    }

    public void TrackOrder(
        SimulatedTradePosition position,
        EntryScorecard card,
        TradeExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(plan);

        _metadata[position] = new EntryMetadata(
            card.TotalScore,
            card.SetupType.ToString(),
            card.TriggerState.ToString(),
            plan.Mode,
            card.StopDistanceBps,
            card.Lines
                .Where(l => l.Group != Domain.Enums.ScoreGroup.Discipline)
                .GroupBy(l => l.CriterionKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().AwardedPoints, StringComparer.Ordinal),
            card.ExpectedCostR,
            card.NetRiskReward,
            card.RiskReward,
            card.Direction?.ToString() ?? string.Empty);
    }

    public BacktestTelemetryReport Build(IReadOnlyList<SimulatedTradePosition> trades)
    {
        if (_built) throw new InvalidOperationException("Telemetry report chỉ được build một lần.");
        _built = true;

        var observations = trades
            .Where(_metadata.ContainsKey)
            .Select(t => new TradeObservation(t, _metadata[t], CostR(t)))
            .ToList();

        var gross = observations.Count == 0
            ? 0m
            : observations.Average(x => x.Trade.RMultiple + x.ActualCostR);
        var friction = observations.Count == 0 ? 0m : observations.Average(x => x.ActualCostR);

        var decisionFingerprint = Convert.ToHexString(_decisionHash.GetHashAndReset()).ToLowerInvariant();
        var tradeFingerprint = TradeFingerprint(observations.Select(x => x.Trade));

        return new BacktestTelemetryReport(
            CurrentSchemaVersion,
            _decisionCount,
            _enteredDecisionCount,
            _metadata.Count,
            decisionFingerprint,
            tradeFingerprint,
            gross,
            friction,
            _decisionBreakdown.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            Group(observations, x => x.Trade.OpenedAtUtc.DayOfWeek.ToString()),
            Group(observations, x => ScoreBand(x.Metadata.Score)),
            Group(observations, x => x.Metadata.Setup),
            Group(observations, x => x.Metadata.Trigger),
            CriterionGroups(observations),
            Deciles(observations, x => x.ActualCostR),
            Deciles(observations.Where(x => x.Metadata.StopDistanceBps is not null).ToList(),
                x => x.Metadata.StopDistanceBps!.Value),
            Excursions(observations),
            EntryFills(observations),
            _candidateEvents.Count,
            _confirmedEvents.Count,
            _enteredEvents.Count,
            observations.Select(Row).ToList());
    }

    private static TelemetryTradeRow Row(TradeObservation observation)
    {
        var trade = observation.Trade;
        var meta = observation.Metadata;

        return new TelemetryTradeRow(
            trade.Symbol,
            trade.OrderPlacedAtUtc,
            meta.Direction,
            meta.Setup,
            meta.Trigger,
            meta.Mode,
            trade.Regime.ToString(),
            meta.Score,
            meta.ExpectedCostR,
            meta.NetRiskReward,
            meta.RiskReward,
            meta.StopDistanceBps,
            trade.SizeR,
            string.Join(';', meta.CriterionPoints
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Value.ToString(CultureInfo.InvariantCulture)}")),
            EntryFillClassifier.Classify(trade).ToString(),
            trade.RMultiple,
            trade.FeeR,
            trade.FundingR,
            trade.SlippageR,
            trade.FilledRiskBudgetR,
            observation.ActualCostR,
            trade.FundingSettlements,
            trade.BarsSinceFirstFill,
            trade.MaxFavorableExcursionR,
            trade.MaxAdverseExcursionR,
            trade.ExitReason?.ToString() ?? string.Empty,
            trade.Outcome?.ToString() ?? string.Empty);
    }

    public void Dispose() => _decisionHash.Dispose();

    private void Count(string key) =>
        _decisionBreakdown[key] = _decisionBreakdown.GetValueOrDefault(key) + 1;

    private static IReadOnlyDictionary<string, TradeGroupStats> Group(
        IReadOnlyList<TradeObservation> observations,
        Func<TradeObservation, string> key) => observations
        .GroupBy(key, StringComparer.Ordinal)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .ToDictionary(
            g => g.Key,
            g => BacktestStatistics.Group(g.Select(x => x.Trade).ToList()),
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, TradeGroupStats> CriterionGroups(
        IReadOnlyList<TradeObservation> observations)
    {
        var groups = new Dictionary<string, List<SimulatedTradePosition>>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            foreach (var (criterion, points) in observation.Metadata.CriterionPoints)
            {
                var key = $"{criterion}:{points}";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<SimulatedTradePosition>();
                list.Add(observation.Trade);
            }
        }

        return groups.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => BacktestStatistics.Group(x.Value), StringComparer.Ordinal);
    }

    private static IReadOnlyList<TelemetryBucketStats> Deciles(
        IReadOnlyList<TradeObservation> observations,
        Func<TradeObservation, decimal> value)
    {
        if (observations.Count == 0) return Array.Empty<TelemetryBucketStats>();
        var sorted = observations.OrderBy(value).ToList();
        var buckets = new List<TelemetryBucketStats>();

        for (var bucket = 0; bucket < 10; bucket++)
        {
            var from = bucket * sorted.Count / 10;
            var to = (bucket + 1) * sorted.Count / 10;
            if (to <= from) continue;
            var slice = sorted.Skip(from).Take(to - from).ToList();
            buckets.Add(new TelemetryBucketStats(
                bucket + 1,
                value(slice[0]),
                value(slice[^1]),
                BacktestStatistics.Group(slice.Select(x => x.Trade).ToList())));
        }

        return buckets;
    }

    private static ExcursionTelemetry Excursions(IReadOnlyList<TradeObservation> observations)
    {
        if (observations.Count == 0) return new ExcursionTelemetry(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        var winners = observations.Where(x => x.Trade.Outcome == Domain.Enums.TradeOutcome.Win).ToList();
        var losers = observations.Where(x => x.Trade.Outcome == Domain.Enums.TradeOutcome.Loss).ToList();

        return new ExcursionTelemetry(
            observations.Average(x => x.Trade.MaxFavorableExcursionR),
            observations.Average(x => x.Trade.MaxAdverseExcursionR),
            AverageOrZero(winners, x => x.Trade.MaxFavorableExcursionR),
            AverageOrZero(winners, x => x.Trade.MaxAdverseExcursionR),
            AverageOrZero(losers, x => x.Trade.MaxFavorableExcursionR),
            AverageOrZero(losers, x => x.Trade.MaxAdverseExcursionR),
            observations.Where(x => x.Trade.BarsToMaxFavorableExcursion is not null)
                .Select(x => (decimal)x.Trade.BarsToMaxFavorableExcursion!.Value).DefaultIfEmpty(0m).Average(),
            observations.Where(x => x.Trade.BarsToMaxAdverseExcursion is not null)
                .Select(x => (decimal)x.Trade.BarsToMaxAdverseExcursion!.Value).DefaultIfEmpty(0m).Average());
    }

    private static EntryFillTelemetry EntryFills(IReadOnlyList<TradeObservation> observations)
    {
        var trades = observations.Select(x => x.Trade).ToList();
        return new EntryFillTelemetry(
            MarketTranchesOffered: trades.Sum(t => t.Entries.Count(x => !x.IsLimit)),
            MarketTranchesFilled: trades.Sum(t => t.Entries.Count(x => !x.IsLimit && x.IsFilled)),
            LimitTranchesOffered: trades.Sum(t => t.LimitTranchesOffered),
            LimitTranchesFilled: trades.Sum(t => t.LimitTranchesFilled),
            LimitTranchesExpired: trades.Sum(t => t.LimitTranchesExpired),
            ByFillState: FillGroups(observations, x => EntryFillClassifier.Classify(x.Trade).ToString()),
            BySetupAndFillState: FillGroups(
                observations,
                x => $"{x.Metadata.Setup}|{EntryFillClassifier.Classify(x.Trade)}"));
    }

    private static IReadOnlyDictionary<string, EntryFillGroupStats> FillGroups(
        IReadOnlyList<TradeObservation> observations,
        Func<TradeObservation, string> key) => observations
        .GroupBy(key, StringComparer.Ordinal)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .ToDictionary(
            g => g.Key,
            g =>
            {
                var values = g.ToList();
                return new EntryFillGroupStats(
                    BacktestStatistics.Group(values.Select(x => x.Trade).ToList()),
                    values.Average(x => x.Trade.RMultiple + x.ActualCostR),
                    values.Average(x => x.ActualCostR),
                    values.Average(x => x.Trade.SizeR <= 0m
                        ? 0m
                        : x.Trade.FilledRiskBudgetR / x.Trade.SizeR),
                    values.Average(x => x.Trade.Entries
                        .Where(e => e.IsLimit && e.IsFilled)
                        .Sum(e => e.RiskWeight)));
            },
            StringComparer.Ordinal);

    private static decimal AverageOrZero(
        IReadOnlyList<TradeObservation> values,
        Func<TradeObservation, decimal> selector) =>
        values.Count == 0 ? 0m : values.Average(selector);

    private static decimal CostR(SimulatedTradePosition trade)
    {
        if (trade.FilledRiskBudgetR <= 0m) return 0m;
        return (trade.FeeR + trade.FundingR + trade.SlippageR) / trade.FilledRiskBudgetR;
    }

    private static string ScoreBand(int score)
    {
        var lower = score / 5 * 5;
        return $"{lower:00}-{lower + 4:00}";
    }

    private static string NumericBand(decimal? value, decimal width)
    {
        if (value is null) return "missing";
        var lower = decimal.Floor(value.Value / width) * width;
        return $"{D(lower)}..{D(lower + width)}";
    }

    private static string TradeFingerprint(IEnumerable<SimulatedTradePosition> trades)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var trade in trades
                     .OrderBy(t => t.OrderPlacedAtUtc)
                     .ThenBy(t => t.Symbol, StringComparer.Ordinal)
                     .ThenBy(t => t.Direction))
        {
            var line = new StringBuilder(280)
                .Append(trade.Symbol).Append('|')
                .Append(trade.OrderPlacedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(trade.OpenedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(trade.ClosedAtUtc?.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(trade.Direction).Append('|').Append(trade.Mode).Append('|')
                .Append(D(trade.Entry)).Append('|').Append(D(trade.InitialStop)).Append('|')
                .Append(D(trade.FirstTarget)).Append('|').Append(D(trade.RunnerTarget)).Append('|')
                .Append(trade.Outcome).Append('|').Append(trade.ExitReason).Append('|')
                .Append(D(trade.RMultiple)).Append('|').Append(D(trade.RealizedR)).Append('|')
                .Append(D(trade.FeeR)).Append('|').Append(D(trade.FundingR)).Append('|')
                .Append(D(trade.SlippageR));
            Append(hash, line.ToString());
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));

    private static string D(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "null";
}
