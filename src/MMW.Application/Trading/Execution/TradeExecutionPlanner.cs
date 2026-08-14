using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Execution;

/// <summary>Một điểm vào của setup, kèm phần NGÂN SÁCH RỦI RO nó được tiêu.</summary>
/// <param name="RiskWeight">
/// Phần ngân sách rủi ro của setup dành cho tranche này; tổng các tranche đúng bằng 1.
/// </param>
/// <remarks>
/// ⚠️ Đây là trọng số RỦI RO, KHÔNG phải trọng số SỐ LƯỢNG. Phân biệt hai thứ này là bắt buộc
/// vì mọi tranche dùng CHUNG một dừng lỗ: tranche vào sâu hơn nằm gần stop hơn, nên cùng một
/// ngân sách rủi ro mua được NHIỀU hợp đồng hơn.
///
/// <code>
/// quantity[i] = (SizeR × RiskWeight[i]) / |Price[i] − StopLoss|
/// </code>
///
/// Chia đều SỐ LƯỢNG như V1 làm tổng lỗ tại stop nhỏ hơn ngân sách: ba tranche cách nhau 0,25R
/// chỉ mất (1 + 0,75 + 0,5)/3 = 0,75R khi khớp đủ, và 0,33R khi chỉ khớp tranche đầu. Hệ quả
/// không phải "vào lệnh nhẹ tay" mà là <c>RealizedR</c> của lệnh scale-in KHÔNG cùng đơn vị với
/// lệnh một điểm vào — mọi con số expectancy gộp chung sau đó đều cộng táo với cam.
///
/// Hệ quả thứ hai, chỉ lộ ra khi tính đúng: tranche vào gần stop có RR hình học tốt hơn nhưng
/// khối lượng lớn hơn, nên tốn PHÍ TÍNH THEO R nhiều hơn. Vào sâu không miễn phí.
/// </remarks>
/// <param name="IsLimit">
/// Chân này là lệnh limit chờ sẵn (phí maker, không trượt giá, có thể không khớp) hay lệnh thị
/// trường (phí taker, chịu trượt giá, chắc chắn khớp).
/// </param>
public sealed record PlannedEntryTranche(decimal Price, decimal RiskWeight, bool IsLimit = false);

/// <summary>Kế hoạch khớp/chốt lệnh tất định sinh từ một phiếu đã qua toàn bộ gate.</summary>
public sealed record TradeExecutionPlan(
    IReadOnlyList<PlannedEntryTranche> Entries,
    decimal StopLoss,
    decimal FirstTakeProfit,
    decimal? RunnerTakeProfit,
    decimal FirstTakeProfitFraction,
    bool MoveRunnerStopToBreakeven,
    string Mode,
    int TrailRunnerPivotBars = 0,
    int? LimitEntryExpiryBars = null);

public interface ITradeExecutionPlanner
{
    TradeExecutionPlan Plan(EntryScorecard card, DailyPlan dailyPlan, EngineSetting settings);

    /// <summary>
    /// Kế hoạch mà đường chạy THẬT thực hiện được đúng như mô tả, hoặc <c>null</c> nếu phiếu
    /// thiếu mức giá nên không đặt được lệnh nào.
    /// </summary>
    /// <remarks>
    /// Tồn tại vì <see cref="Plan"/> mô tả một thứ mà bộ đặt lệnh thật CHƯA làm được: kế hoạch
    /// nhiều chân, chân sau là lệnh chờ. Trình mô phỏng backtest thực hiện đầy đủ (nó theo dõi
    /// khớp từng chân, phí maker/taker riêng, hết hạn lệnh chờ), còn <c>Trade</c> chỉ có MỘT
    /// <c>EntryPrice</c> và MỘT <c>Quantity</c> nên chạy thật luôn gộp về một lệnh thị trường.
    ///
    /// Hệ quả đo được trên phiếu 13:31 ngày 14/08/2026: cổng chi phí chấm kế hoạch 2 chân và
    /// thấy gross 1,960R / netRR 1,287, trong khi lệnh sẽ thật sự chạy chỉ có gross 1,608R /
    /// netRR 1,019 — cổng lạc quan hơn thực tế 26%. Một cổng đo thứ không chạy thì con số nó
    /// tạo ra không dùng để kết luận được điều gì.
    ///
    /// Vì vậy: backtest tiếp tục dùng <see cref="Plan"/> (giữ nguyên để mọi số liệu lịch sử còn
    /// so sánh được), còn đường thật dùng hàm này cho CẢ cổng chi phí LẪN lúc đặt lệnh — cùng
    /// một đối tượng, nên hai bên không thể lệch nhau nữa.
    ///
    /// Đây cũng là chỗ duy nhất cần sửa để chuyển sang vào bằng lệnh chờ: đổi <c>IsLimit</c>
    /// của chân duy nhất, cổng và bộ đặt lệnh tự khớp theo.
    /// </remarks>
    TradeExecutionPlan? PlanLive(EntryScorecard card);
}

/// <summary>
/// Biến quyết định vào lệnh thành kế hoạch thực thi thích nghi theo regime. Không I/O, không
/// đồng hồ và không tự tăng tổng rủi ro.
/// </summary>
public sealed class TradeExecutionPlanner : ITradeExecutionPlanner
{
    /// <summary>Sàn mục tiêu ngày đi ngang khi phiếu không mang theo mức cấu trúc nào.</summary>
    private const decimal RangeTargetR = 1m;
    private const decimal TrendFirstTargetR = 1.5m;
    private const decimal TrendRunnerTargetR = 3m;
    private const decimal TrendPartialFraction = 0.5m;
    private const decimal ScaleInStepR = 0.25m;
    private const int StrongStructurePoints = 8;

    /// <summary>Nhãn <see cref="TradeExecutionPlan.Mode"/> của kế hoạch chạy thật.</summary>
    public const string LiveMode = "LiveSingleMarket";

    /// <inheritdoc />
    public TradeExecutionPlan? PlanLive(EntryScorecard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Ba mức giá là điều kiện cần của chính bộ đặt lệnh: LiveOrderService chặn thẳng lệnh
        // thiếu dừng lỗ hoặc chốt lời. Trả null thay vì ném để một phiếu thiếu giá không giết
        // cả chu kỳ chấm điểm — và để cổng chi phí lẫn bộ đặt lệnh cùng đọc MỘT câu trả lời cho
        // câu hỏi "phiếu này có chạy thật được không".
        if (card.SuggestedEntry is not { } entry || entry <= 0m) return null;
        if (card.SuggestedStopLoss is not { } stop || stop <= 0m) return null;
        if (card.SuggestedTakeProfit is not { } target || target <= 0m) return null;
        if (Math.Abs(entry - stop) <= 0m) return null;

        // Một chân, toàn bộ ngân sách rủi ro, lệnh thị trường — đúng bằng những gì
        // ScorecardExecutionService gửi sàn. Không runner, không chốt từng phần: Trade không có
        // chỗ lưu chúng, nên hứa hẹn ở đây sẽ lại thành một lời hứa suông nữa.
        return new TradeExecutionPlan(
            [new PlannedEntryTranche(entry, 1m, IsLimit: false)],
            stop,
            target,
            RunnerTakeProfit: null,
            FirstTakeProfitFraction: 1m,
            MoveRunnerStopToBreakeven: false,
            Mode: LiveMode);
    }

    public TradeExecutionPlan Plan(EntryScorecard card, DailyPlan dailyPlan, EngineSetting settings)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(dailyPlan);
        ArgumentNullException.ThrowIfNull(settings);

        if (card.Outcome != ScorecardOutcome.Entered
            || card.Direction is not { } direction
            || card.SuggestedEntry is not { } entry
            || card.SuggestedStopLoss is not { } stop)
        {
            throw new ArgumentException("Phiếu chưa đủ điều kiện/mức giá để lập kế hoạch thực thi.", nameof(card));
        }

        var unitRisk = Math.Abs(entry - stop);
        if (unitRisk <= 0m)
            throw new ArgumentException("Khoảng cách entry–stop phải lớn hơn 0.", nameof(card));

        if (settings.StrategyVersion.UsesSidewaysV6())
            return PlanV6(card, dailyPlan, settings, direction, entry, stop, unitRisk);

        if (settings.StrategyVersion.UsesTriggerFirst())
            return PlanV3(card, dailyPlan, settings, direction, entry, stop, unitRisk);

        var regime = card.EffectiveDayRegime ?? dailyPlan.DayRegime;
        var isRange = regime == DayRegime.Range;
        var isTrendAligned =
            (regime == DayRegime.TrendUp && direction == TradeDirection.Long)
            || (regime == DayRegime.TrendDown && direction == TradeDirection.Short);

        var structure = Points(card, "technical.market_structure");
        var volume = Points(card, "technical.volume_confirmation");
        var strongTrend = isTrendAligned
                          && structure >= StrongStructurePoints
                          && volume >= 5;

        if (isRange)
        {
            // KHÔNG còn chốt cứng tại 1R như V1. Phiếu đã phải qua `technical.structural_room`
            // với R:R tối thiểu 1,6 mới tới được đây, nên chốt tại 1R là tự nguyện vứt đi phần
            // chỗ chạy vừa được kiểm chứng — và tệ hơn, 1R là mức mà toán chi phí nói rõ không
            // thắng nổi: phí taker hai chiều cộng trượt giá đẩy tỉ lệ thắng hoà vốn lên khoảng
            // 72%. Dùng mục tiêu cấu trúc, và chỉ lùi về bội R khi phiếu không có mức nào.
            var rangeTarget = card.SuggestedTakeProfit
                              ?? AtR(entry, unitRisk, direction, Math.Max(RangeTargetR, settings.MinStructuralRr));

            var rangeEntry = SafeLimitEntry(
                card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.10m);
            return new TradeExecutionPlan(
                [new PlannedEntryTranche(rangeEntry, 1m, IsLimit: true)],
                stop, rangeTarget, null, 1m, false, "RangeQuick",
                LimitEntryExpiryBars: 8);
        }

        if (!strongTrend)
        {
            var standardFirstTarget = card.SuggestedFirstTakeProfit
                              ?? card.SuggestedTakeProfit
                              ?? AtR(entry, unitRisk, direction, TrendFirstTargetR);
            var standardRunnerTarget = card.SuggestedRunnerTakeProfit;
            var hasRunner = standardRunnerTarget is not null && standardRunnerTarget != standardFirstTarget;
            var limitEntry = SafeLimitEntry(
                card.SuggestedLimitEntry, entry, stop, direction, ScaleInStepR);
            return new TradeExecutionPlan(
                [new PlannedEntryTranche(limitEntry, 1m, IsLimit: true)],
                stop,
                standardFirstTarget,
                hasRunner ? standardRunnerTarget : null,
                hasRunner ? TrendPartialFraction : 1m,
                hasRunner,
                "Standard",
                TrailRunnerPivotBars: hasRunner ? 3 : 0,
                LimitEntryExpiryBars: settings.LimitEntryExpiryBars);
        }

        // Chia đều NGÂN SÁCH RỦI RO, không chia đều số lượng — xem chú thích của
        // `PlannedEntryTranche`. Phần dư của phép chia dồn vào tranche cuối để tổng đúng bằng 1
        // theo số thập phân, không phải xấp xỉ.
        // Chân đầu là lệnh thị trường tại thời điểm phiếu được chấp nhận; các chân sau là lệnh
        // limit chờ giá hồi về. Phân biệt này quyết định phí (taker/maker), trượt giá (có/không)
        // và cả việc chân đó có chắc chắn khớp hay không.
        var retestEntry = SafeLimitEntry(
            card.SuggestedLimitEntry, entry, stop, direction, ScaleInStepR);
        var entries = new List<PlannedEntryTranche>
        {
            new(entry, 0.60m),
            new(retestEntry, 0.40m, IsLimit: true),
        };

        var firstTarget = AtR(entry, unitRisk, direction, TrendFirstTargetR);
        var structuralRunner = card.SuggestedRunnerTakeProfit ?? card.SuggestedTakeProfit;
        var runnerTarget = structuralRunner is { } candidate
                           && Math.Abs(candidate - entry) > Math.Abs(firstTarget - entry)
            ? candidate
            : AtR(entry, unitRisk, direction, TrendRunnerTargetR);

        return new TradeExecutionPlan(
            entries,
            stop,
            firstTarget,
            runnerTarget,
            TrendPartialFraction,
            true,
            "StrongTrendRunner",
            TrailRunnerPivotBars: 3,
            LimitEntryExpiryBars: settings.LimitEntryExpiryBars);
    }

    private static TradeExecutionPlan PlanV3(
        EntryScorecard card,
        DailyPlan dailyPlan,
        EngineSetting settings,
        TradeDirection direction,
        decimal entry,
        decimal stop,
        decimal unitRisk)
    {
        if (card.SetupType == SetupType.RangeRejection)
        {
            var target = card.SuggestedTakeProfit
                         ?? AtR(entry, unitRisk, direction, settings.MinStructuralRr);
            var limit = SafeLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.10m);
            return new TradeExecutionPlan(
                [
                    new PlannedEntryTranche(entry, 0.60m),
                    new PlannedEntryTranche(limit, 0.40m, IsLimit: true),
                ],
                stop,
                target,
                null,
                1m,
                false,
                "RangeRejectionV3",
                LimitEntryExpiryBars: Math.Min(4, settings.LimitEntryExpiryBars));
        }

        if (card.SetupType == SetupType.TrendPullback)
        {
            var limit = SafeLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, ScaleInStepR);
            var firstTarget = card.SuggestedFirstTakeProfit
                              ?? card.SuggestedTakeProfit
                              ?? AtR(entry, unitRisk, direction, TrendFirstTargetR);
            var runner = card.SuggestedRunnerTakeProfit;
            var hasRunner = runner is { } pullbackRunner
                            && Math.Abs(pullbackRunner - entry) > Math.Abs(firstTarget - entry);
            var fraction = hasRunner
                ? DynamicFirstTargetFraction(card, settings, entry, firstTarget)
                : 1m;

            return new TradeExecutionPlan(
                [
                    new PlannedEntryTranche(entry, 0.50m),
                    new PlannedEntryTranche(limit, 0.50m, IsLimit: true),
                ],
                stop,
                firstTarget,
                hasRunner ? runner : null,
                fraction,
                hasRunner,
                "TrendPullbackV3",
                TrailRunnerPivotBars: hasRunner ? 3 : 0,
                LimitEntryExpiryBars: Math.Min(4, settings.LimitEntryExpiryBars));
        }

        if (card.SetupType != SetupType.StrongTrendBreakout)
            throw new ArgumentException(
                $"V3 chỉ lập lệnh cho setup đã xác nhận; nhận {card.SetupType}.", nameof(card));

        var retest = SafeLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, ScaleInStepR);
        var entries = new List<PlannedEntryTranche>
        {
            new(entry, 0.60m),
            new(retest, 0.40m, IsLimit: true),
        };
        var strongFirst = AtR(entry, unitRisk, direction, TrendFirstTargetR);
        var structuralRunner = card.SuggestedRunnerTakeProfit ?? card.SuggestedTakeProfit;
        var strongRunner = structuralRunner is { } strongCandidate
                           && Math.Abs(strongCandidate - entry) > Math.Abs(strongFirst - entry)
            ? strongCandidate
            : AtR(entry, unitRisk, direction, TrendRunnerTargetR);

        return new TradeExecutionPlan(
            entries,
            stop,
            strongFirst,
            strongRunner,
            DynamicFirstTargetFraction(card, settings, entry, strongFirst),
            true,
            "StrongTrendRunnerV3",
            TrailRunnerPivotBars: 3,
            LimitEntryExpiryBars: Math.Min(4, settings.LimitEntryExpiryBars));
    }

    private static TradeExecutionPlan PlanV6(
        EntryScorecard card,
        DailyPlan dailyPlan,
        EngineSetting settings,
        TradeDirection direction,
        decimal entry,
        decimal stop,
        decimal unitRisk)
    {
        if (card.SetupType == SetupType.RectangleRangeFade)
        {
            var boundary = SafeLimitEntry(
                card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.10m);
            var first = card.SuggestedFirstTakeProfit
                        ?? AtR(entry, unitRisk, direction, 1m);
            var runner = card.SuggestedRunnerTakeProfit;
            var hasRunner = runner is { } candidate
                            && Math.Abs(candidate - entry) > Math.Abs(first - entry);

            return new TradeExecutionPlan(
                [
                    new PlannedEntryTranche(entry, 0.50m),
                    new PlannedEntryTranche(boundary, 0.50m, IsLimit: true),
                ],
                stop,
                first,
                hasRunner ? runner : null,
                hasRunner ? 0.60m : 1m,
                hasRunner,
                "RectangleRangeFadeV6",
                TrailRunnerPivotBars: 0,
                LimitEntryExpiryBars: Math.Min(2, settings.LimitEntryExpiryBars));
        }

        if (card.SetupType is SetupType.RectangleBreakout or SetupType.TriangleBreakout)
        {
            var retest = SafeLimitEntry(
                card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.20m);
            var first = card.SuggestedFirstTakeProfit
                        ?? AtR(entry, unitRisk, direction, 1.20m);
            var runner = card.SuggestedRunnerTakeProfit;
            var hasRunner = runner is { } candidate
                            && Math.Abs(candidate - entry) > Math.Abs(first - entry);

            return new TradeExecutionPlan(
                [
                    new PlannedEntryTranche(entry, 0.60m),
                    new PlannedEntryTranche(retest, 0.40m, IsLimit: true),
                ],
                stop,
                first,
                hasRunner ? runner : null,
                hasRunner ? DynamicFirstTargetFraction(card, settings, entry, first) : 1m,
                hasRunner,
                card.SetupType == SetupType.TriangleBreakout
                    ? "TriangleBreakoutV6"
                    : "RectangleBreakoutV6",
                TrailRunnerPivotBars: hasRunner ? 3 : 0,
                LimitEntryExpiryBars: Math.Min(3, settings.LimitEntryExpiryBars));
        }

        // Trend của V6 giữ nguyên execution V3; chỉ admission/sizing khác.
        return PlanV3(card, dailyPlan, settings, direction, entry, stop, unitRisk);
    }

    /// <summary>
    /// Chốt phần nhỏ nhất trong [30%, 60%] đủ khóa LockedNetRMin sau expected cost. ExpectedCostR
    /// được tính từ đúng plan ở SignalEval; lần gọi đầu chưa có số đo dùng 0 và không ảnh hưởng
    /// entry/stop/target hay cost gate.
    /// </summary>
    private static decimal DynamicFirstTargetFraction(
        EntryScorecard card,
        EngineSetting settings,
        decimal entry,
        decimal firstTarget)
    {
        if (card.SuggestedStopLoss is not { } stop) return TrendPartialFraction;
        var risk = Math.Abs(entry - stop);
        if (risk <= 0m) return TrendPartialFraction;

        var grossTpR = Math.Abs(firstTarget - entry) / risk;
        if (grossTpR <= 0m) return TrendPartialFraction;

        var required = (settings.V3LockedNetRMin + (card.ExpectedCostR ?? 0m)) / grossTpR;
        return Math.Clamp(required, 0.30m, 0.60m);
    }

    private static int Points(EntryScorecard card, string key) =>
        card.Lines.FirstOrDefault(l => l.CriterionKey == key)?.AwardedPoints ?? 0;

    private static decimal AtR(decimal entry, decimal risk, TradeDirection direction, decimal r) =>
        direction == TradeDirection.Long ? entry + risk * r : entry - risk * r;

    private static decimal Pullback(decimal entry, decimal risk, TradeDirection direction, decimal r) =>
        direction == TradeDirection.Long ? entry - risk * r : entry + risk * r;

    /// <summary>
    /// Kẹp limit khỏi vùng sát stop, nơi quantity = risk/distance sẽ nổ ra ngoài ý muốn.
    /// Chốt này khớp đúng invariant của simulator và phải tồn tại ngay ở planner để chạy thật
    /// không thể sinh một kế hoạch mà chính mô hình rủi ro từ chối.
    /// </summary>
    private static decimal SafeLimitEntry(
        decimal? suggested,
        decimal entry,
        decimal stop,
        TradeDirection direction,
        decimal fallbackPullbackR)
    {
        var unitRisk = Math.Abs(entry - stop);
        var candidate = suggested ?? Pullback(entry, unitRisk, direction, fallbackPullbackR);
        var stopFloor = unitRisk * 0.25m;

        if (direction == TradeDirection.Long)
        {
            candidate = Math.Max(candidate, stop + stopFloor);
            return candidate < entry
                ? candidate
                : Pullback(entry, unitRisk, direction, Math.Max(0.10m, fallbackPullbackR));
        }

        candidate = Math.Min(candidate, stop - stopFloor);
        return candidate > entry
            ? candidate
            : Pullback(entry, unitRisk, direction, Math.Max(0.10m, fallbackPullbackR));
    }
}
