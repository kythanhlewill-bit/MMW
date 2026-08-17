using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Execution;

/// <summary>Kết quả setup-specific core gate của V3.</summary>
public sealed record SetupTriggerDecision(
    bool Passed,
    SetupType SetupType,
    SetupTriggerState State,
    string DetailVi,
    decimal? SuggestedLimitEntry = null,
    SetupFunnelStage Stage = SetupFunnelStage.NotEligible,
    string? EventId = null,
    int SetupQualityScore = 0,
    decimal? SuggestedStopLoss = null,
    decimal? SuggestedFirstTakeProfit = null,
    decimal? SuggestedRunnerTakeProfit = null)
{
    public static SetupTriggerDecision Reject(
        SetupTriggerState state,
        string detail,
        SetupType setupType = SetupType.None,
        SetupFunnelStage? stage = null,
        string? eventId = null,
        int setupQualityScore = 0) =>
        new(false, setupType, state, detail, Stage: stage ?? StageFor(state), EventId: eventId,
            SetupQualityScore: setupQualityScore);

    private static SetupFunnelStage StageFor(SetupTriggerState state) => state switch
    {
        SetupTriggerState.NoBreakOfStructure or SetupTriggerState.RangeGeometryWeak
            or SetupTriggerState.CompressionMissing => SetupFunnelStage.EligibleContext,
        SetupTriggerState.BreakUnretested or SetupTriggerState.RangeNotSwept
            or SetupTriggerState.BreakoutMissing => SetupFunnelStage.StructureCandidate,
        SetupTriggerState.NotEvaluated => SetupFunnelStage.NotEligible,
        _ => SetupFunnelStage.TriggerStarted,
    };
}

public interface ISetupTriggerPolicy
{
    /// <summary>Hàm thuần: context chỉ chứa nến đã đóng và range đã biết tại đúng thời điểm chấm.</summary>
    SetupTriggerDecision Evaluate(ScoringContext context, RangeLocation? range);
}

/// <summary>
/// V3 không cho điểm bối cảnh thay thế event vào lệnh. Range phải có sweep/rejection; trend phải
/// có BOS, retest mới, impulse đủ lực, pullback co volume và reclaim đóng nến.
/// </summary>
public sealed class SetupTriggerPolicy : ISetupTriggerPolicy
{
    private const int VolumeLookbackBars = 20;
    private const decimal ReclaimCloseZone = 0.25m;

    private readonly MarketStructureAnalyzer _structure;
    private readonly ISidewaysPatternAnalyzer _sideways;

    public SetupTriggerPolicy(MarketStructureAnalyzer structure, ISidewaysPatternAnalyzer sideways)
    {
        _structure = structure;
        _sideways = sideways;
    }

    public SetupTriggerDecision Evaluate(ScoringContext context, RangeLocation? range)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.EntryCandles.Count < VolumeLookbackBars + 2)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.ImpulseWeak,
                $"V3 cần ít nhất {VolumeLookbackBars + 2} nến đã đóng để xác nhận trigger.");

        // Rẽ theo CẤU TRÚC ngày, không theo nhãn ngày. Trước đây chỗ này so thẳng với
        // DayRegime.Range, nên hai nhãn nguy hiểm — EventDay và HighVolatility — rơi hết vào
        // nhánh xu hướng rồi bị EvaluateTrend bác ngay dòng đầu, vì chúng cũng không phải
        // TrendUp/TrendDown. Kết quả: ngày có tin không có playbook nào cả. Xem DayPlaybook.
        // Thử nhịp hồi-về-MA TRƯỚC, nhưng chỉ nhận khi nó XÁC NHẬN. Không xác nhận thì rơi
        // xuống nguyên đường cũ, giữ nguyên lý do từ chối của đường đó.
        //
        // Cách ghép thuần cộng thêm này là có chủ ý: đường cũ đã chạy thật 8 ngày và mọi chẩn
        // đoán đang dựa vào các mã trạng thái của nó. Nếu để bộ dò mới ghi đè cả những lần nó
        // TỪ CHỐI, mọi thống kê "chặn ở cổng nào" sẽ đứt gãy đúng lúc cần so sánh trước/sau nhất.
        var maPullback = EvaluateMaPullback(context);
        if (maPullback.Passed) return maPullback;

        var structure = DayPlaybook.StructureOf(context.DailyPlan);

        var fallback = structure == DayStructure.Range
            ? context.Settings.StrategyVersion.UsesSidewaysV6()
                ? EvaluateSidewaysV6(context)
                : EvaluateRange(context, range)
            : EvaluateTrend(context, structure);

        // Nhánh MA không xác nhận thì KHÔNG được thắng — kể cả khi nó có lý do nghe thuyết phục.
        // Đường cũ vẫn có thể tìm ra một setup hợp lệ trên chính cây nến đó, và để nhánh mới bác
        // thay sẽ giết đúng những lệnh nó lẽ ra phải thêm vào.
        //
        // Nhưng lý do của nó vẫn phải hiện ra, nếu không sẽ không có cách nào biết vì sao nhịp
        // hồi chẳng bao giờ kích hoạt. Nên: giữ nguyên State/SetupType/Stage của đường cũ (mọi
        // thống kê "chặn ở cổng nào" còn so sánh được trước/sau), chỉ ghi kèm vào phần mô tả.
        return maPullback.SetupType == SetupType.MaPullback
            ? fallback with { DetailVi = $"{fallback.DetailVi} · Nhịp MA: {maPullback.DetailVi}" }
            : fallback;
    }

    // ── Nhịp hồi về MA nhanh ────────────────────────────────────────────

    private const int MaFastPeriod = 7;
    private const int MaSlowPeriod = 25;

    /// <summary>Số nến tối đa kể từ lúc MA cắt nhau. 40 nến 15m = 10 giờ.</summary>
    /// <remarks>
    /// Nhịp này ăn theo lực của cú đẩy vừa sinh ra xu hướng. Quá mốc đó thì cú đẩy đã tiêu hoá
    /// xong, và "giá chạm MA7" chỉ còn là giá đi ngang quanh MA chứ không phải một nhịp hồi.
    /// </remarks>
    private const int MaPullbackMaxBarsSinceCross = 40;

    /// <summary>Cửa sổ đọc đáy vùng tích luỹ để đặt dừng lỗ.</summary>
    private const int MaPullbackZoneBars = 10;

    /// <summary>Bội R của mục tiêu.</summary>
    private const decimal MaPullbackTargetR = 2m;

    /// <summary>
    /// Xu hướng đọc từ MA7/MA25, vào khi giá hồi về chạm MA7.
    /// </summary>
    /// <remarks>
    /// Dừng lỗ đặt Ở GIỮA đáy xoay gần nhất và đáy vùng tích luỹ — đáy xoay một mình thì quá sát
    /// (khối lượng nổ, phí ăn hết), còn đáy vùng một mình thì rộng tay hơn mức cần. Sau đó áp
    /// sàn <c>MinStopDistancePercent</c> ngay tại đây: quyết định này mang theo dừng lỗ riêng và
    /// nó GHI ĐÈ mức của <c>StructuralLevelPlanner</c>, nên sàn dựng ở planner không với tới.
    /// </remarks>
    private SetupTriggerDecision EvaluateMaPullback(ScoringContext context)
    {
        var candles = context.EntryCandles;

        // Chỉ cần đủ nến để TÍNH được chồng MA. Việc dò ngược tìm điểm cắt tự dừng khi hết dữ
        // liệu và trả về "không thấy" — đòi sẵn cả cửa sổ dò ở đây sẽ tắt bộ dò suốt 40 nến đầu
        // sau mỗi lần khởi động, đúng lúc không ai nhìn nhật ký.
        if (candles.Count < MaSlowPeriod + 2)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaTrendMissing, "Chưa đủ nến để dựng chồng MA.");

        var isLong = context.Direction == TradeDirection.Long;
        var last = candles.Count - 1;

        var fast = Sma(candles, MaFastPeriod, last);
        var slow = Sma(candles, MaSlowPeriod, last);
        if (fast <= 0m || slow <= 0m)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaTrendMissing, "Không tính được MA.");

        // (1) Chồng MA phải xếp thuận chiều đang xét.
        if (isLong ? fast <= slow : fast >= slow)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaTrendMissing,
                $"MA{MaFastPeriod}={fast:N2} và MA{MaSlowPeriod}={slow:N2} không xếp thuận " +
                $"chiều {context.Direction}.");

        // (2) Lần cắt gần nhất phải còn mới.
        var barsSinceCross = BarsSinceMaCross(candles, last, isLong);
        if (barsSinceCross is not { } sinceCross)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackStale,
                $"Không tìm thấy lần MA cắt nhau trong {MaPullbackMaxBarsSinceCross} nến gần đây.");

        if (sinceCross > MaPullbackMaxBarsSinceCross)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackStale,
                $"MA cắt cách đây {sinceCross} nến, quá mốc {MaPullbackMaxBarsSinceCross}.");

        // (3) Cú đẩy sinh ra xu hướng phải có khối lượng thật.
        var minVolume = context.Settings.V6BreakoutMinRelativeVolume;
        var impulseVolume = 0m;
        for (var i = last - sinceCross; i <= last; i++)
            impulseVolume = Math.Max(impulseVolume, RelativeVolume(candles, i));

        if (impulseVolume < minVolume)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaImpulseWeak,
                $"Cú đẩy sau khi MA cắt chỉ đạt khối lượng {impulseVolume:N2}× trung bình, " +
                $"cần {minVolume:N2}×.",
                SetupType.MaPullback,
                SetupFunnelStage.StructureCandidate);

        // (4) Giá phải đang CHẠM MA nhanh — thân nến vẫn giữ phía đúng của MA chậm.
        var current = candles[last];
        var touching = current.Low <= fast && current.High >= fast;
        if (!touching)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackMissing,
                $"Giá chưa hồi về chạm MA{MaFastPeriod}={fast:N2} " +
                $"(nến hiện tại {current.Low:N2}–{current.High:N2}).",
                SetupType.MaPullback,
                SetupFunnelStage.StructureCandidate);

        if (isLong ? current.Close < slow : current.Close > slow)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackMissing,
                $"Giá đóng {current.Close:N2} đã xuyên qua MA{MaSlowPeriod}={slow:N2} — " +
                "đây là gãy xu hướng chứ không phải nhịp hồi.",
                SetupType.MaPullback,
                SetupFunnelStage.TriggerStarted);

        // (5) Dừng lỗ: giữa đáy xoay gần nhất và đáy vùng tích luỹ.
        var entry = context.CurrentPrice;
        var zone = candles.Skip(Math.Max(0, candles.Count - MaPullbackZoneBars)).ToList();
        var zoneEdge = isLong ? zone.Min(c => c.Low) : zone.Max(c => c.High);

        var pivots = _structure.Swings.Detect(
            candles.TakeLast(MaSlowPeriod * 2).ToList(), context.Settings.SwingPivotBars);
        var swingEdge = isLong
            ? pivots.Where(p => !p.IsHigh && p.Price < entry).Select(p => (decimal?)p.Price).Max()
            : pivots.Where(p => p.IsHigh && p.Price > entry).Select(p => (decimal?)p.Price).Min();

        // Không có đáy xoay thì dùng thẳng đáy vùng — không bịa ra một nửa của thứ không tồn tại.
        var stop = swingEdge is { } swing ? (swing + zoneEdge) / 2m : zoneEdge;

        var atr = AverageTrueRange(candles, 14);
        var floor = entry * context.Settings.MinStopDistancePercent / 100m;
        if (Math.Abs(entry - stop) < floor)
            stop = isLong ? entry - floor : entry + floor;

        var distance = Math.Abs(entry - stop);
        if (distance <= 0m)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackMissing, "Dừng lỗ trùng giá vào.",
                SetupType.MaPullback, SetupFunnelStage.TriggerStarted);

        if (atr > 0m && distance > atr * context.Settings.StopAtrMultipleMax)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.MaPullbackMissing,
                $"Dừng lỗ cách {distance / entry * 100m:N2}% vượt trần " +
                $"{context.Settings.StopAtrMultipleMax:N2} ATR.",
                SetupType.MaPullback, SetupFunnelStage.TriggerStarted);

        var target = isLong
            ? entry + distance * MaPullbackTargetR
            : entry - distance * MaPullbackTargetR;

        var quality = MaPullbackQuality(impulseVolume, minVolume, fast, slow, sinceCross);

        return new SetupTriggerDecision(
            Passed: true,
            SetupType: SetupType.MaPullback,
            State: SetupTriggerState.Confirmed,
            DetailVi:
                $"Hồi về MA{MaFastPeriod} xác nhận: MA{MaFastPeriod}={fast:N2} trên " +
                $"MA{MaSlowPeriod}={slow:N2}, cắt cách {sinceCross} nến, khối lượng đẩy " +
                $"{impulseVolume:N2}×, dừng lỗ {stop:N2} ({distance / entry * 100m:N2}%), " +
                $"chốt lời {target:N2} ({MaPullbackTargetR:N1}R).",
            // Vào bằng lệnh chờ ngay tại MA nhanh: đó chính là mức mà phương pháp này chờ giá về.
            SuggestedLimitEntry: fast,
            Stage: SetupFunnelStage.Confirmed,
            EventId: $"{context.Symbol}:MaPullback:{context.Direction}:{last - sinceCross}",
            SetupQualityScore: quality,
            SuggestedStopLoss: stop,
            SuggestedFirstTakeProfit: target);
    }

    /// <summary>
    /// Chất lượng 60–100. Sàn 60 vì một setup đã qua hết năm điều kiện trên không thể bị
    /// <c>QualityMultiplier</c> của V6 cho về 0 chỉ vì thang điểm này không có phần hình học.
    /// </summary>
    private static int MaPullbackQuality(
        decimal impulseVolume, decimal minVolume, decimal fast, decimal slow, int barsSinceCross)
    {
        // Khối lượng vượt ngưỡng bao nhiêu (tối đa 20 điểm).
        var volumeScore = Math.Min(20m, (impulseVolume / minVolume - 1m) * 40m);

        // Hai MA tách nhau càng rõ, xu hướng càng sạch (tối đa 12 điểm).
        var separation = fast <= 0m ? 0m : Math.Abs(fast - slow) / fast * 100m;
        var separationScore = Math.Min(12m, separation * 30m);

        // Nhịp càng sớm sau khi cắt càng tốt (tối đa 8 điểm).
        var freshScore = Math.Max(0m, 8m * (1m - (decimal)barsSinceCross / MaPullbackMaxBarsSinceCross));

        return (int)Math.Clamp(60m + volumeScore + separationScore + freshScore, 60m, 100m);
    }

    /// <summary>Số nến kể từ lần MA nhanh cắt MA chậm theo chiều đang xét, hoặc null nếu không thấy.</summary>
    private static int? BarsSinceMaCross(IReadOnlyList<Candle> candles, int last, bool isLong)
    {
        for (var back = 1; back <= MaPullbackMaxBarsSinceCross; back++)
        {
            var i = last - back;
            if (i - 1 < MaSlowPeriod) break;

            var fastNow = Sma(candles, MaFastPeriod, i);
            var slowNow = Sma(candles, MaSlowPeriod, i);
            var fastPrev = Sma(candles, MaFastPeriod, i - 1);
            var slowPrev = Sma(candles, MaSlowPeriod, i - 1);

            var crossed = isLong
                ? fastNow > slowNow && fastPrev <= slowPrev
                : fastNow < slowNow && fastPrev >= slowPrev;

            if (crossed) return back;
        }

        return null;
    }

    private static decimal Sma(IReadOnlyList<Candle> candles, int period, int index)
    {
        if (index < period - 1 || period <= 0) return 0m;
        var sum = 0m;
        for (var i = index - period + 1; i <= index; i++) sum += candles[i].Close;
        return sum / period;
    }

    private SetupTriggerDecision EvaluateSidewaysV6(ScoringContext context)
    {
        var atr = AverageTrueRange(context.EntryCandles, 14);
        if (atr <= 0m)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RangeGeometryWeak,
                "Không tính được ATR để dựng pattern sideway V6.");

        var breakout = EvaluateCompressionBreakout(context, atr);
        var fade = EvaluateRectangleFade(context, atr);

        if (breakout.Passed && fade.Passed)
            return breakout.SetupQualityScore >= fade.SetupQualityScore ? breakout : fade;
        if (breakout.Passed) return breakout;
        if (fade.Passed) return fade;

        return new[] { breakout, fade }
            .OrderByDescending(x => x.Stage)
            .ThenByDescending(x => x.SetupQualityScore)
            .ThenBy(x => (int)x.SetupType)
            .First();
    }

    private SetupTriggerDecision EvaluateRectangleFade(ScoringContext context, decimal atr)
    {
        var candles = context.EntryCandles;
        var currentIndex = candles.Count - 1;
        SidewaysPattern? latestPattern = null;

        for (var sweepIndex = Math.Max(context.Settings.V6PatternLookbackBars,
                 currentIndex - context.Settings.V6RangeSweepLookbackBars + 1);
             sweepIndex <= currentIndex;
             sweepIndex++)
        {
            var pattern = _sideways.Detect(
                candles, sweepIndex, context.Settings, atr, SidewaysPatternKind.Rectangle);
            if (pattern is null) continue;
            latestPattern = pattern;

            var sweep = candles[sweepIndex];
            var swept = context.Direction == TradeDirection.Long
                ? sweep.Low <= pattern.FloorAtEnd
                : sweep.High >= pattern.UpperAtEnd;
            if (!swept) continue;

            var eventId = $"{context.Symbol}:{pattern.EventKey}:Fade:{context.Direction}";
            var boundary = context.Direction == TradeDirection.Long
                ? pattern.FloorAtEnd
                : pattern.UpperAtEnd;

            var firstConfirmation = -1;
            for (var i = sweepIndex; i <= currentIndex; i++)
            {
                if (IsRangeConfirmation(candles, i, context.Direction, boundary, context.Settings))
                {
                    firstConfirmation = i;
                    break;
                }
            }

            if (firstConfirmation != currentIndex)
            {
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.RangeConfirmationMissing,
                    firstConfirmation >= 0
                        ? "Range event đã được xác nhận ở nến trước; không phát lại cùng một setup."
                        : $"Đã sweep biên {boundary:N2} nhưng chưa có nến confirmation hợp lệ.",
                    SetupType.RectangleRangeFade,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    pattern.GeometryQuality);
            }

            var confirmation = candles[currentIndex];
            var body = BodyRatio(confirmation);
            var close = CloseLocation(confirmation);
            var relativeVolume = RelativeVolume(candles, currentIndex);
            var sweepExtreme = context.Direction == TradeDirection.Long
                ? candles.Skip(sweepIndex).Take(currentIndex - sweepIndex + 1).Min(c => c.Low)
                : candles.Skip(sweepIndex).Take(currentIndex - sweepIndex + 1).Max(c => c.High);
            var stop = context.Direction == TradeDirection.Long
                ? sweepExtreme - atr * context.Settings.V6RangeStopBufferAtr
                : sweepExtreme + atr * context.Settings.V6RangeStopBufferAtr;
            var entry = confirmation.Close;
            var risk = Math.Abs(entry - stop);
            if (risk <= 0m)
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.RangeRejectionWeak,
                    "Range confirmation không dựng được stop hợp lệ.",
                    SetupType.RectangleRangeFade,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    pattern.GeometryQuality);

            var firstTarget = pattern.Midpoint;
            var runner = context.Direction == TradeDirection.Long
                ? pattern.UpperAtEnd
                : pattern.FloorAtEnd;
            var roomR = context.Direction == TradeDirection.Long
                ? (firstTarget - entry) / risk
                : (entry - firstTarget) / risk;
            var penetrationAtr = context.Direction == TradeDirection.Long
                ? Math.Max(0m, pattern.FloorAtEnd - sweep.Low) / atr
                : Math.Max(0m, sweep.High - pattern.UpperAtEnd) / atr;
            var quality = ClampScore(
                pattern.GeometryQuality * 0.40m
                + Math.Min(15m, penetrationAtr * 30m)
                + Math.Min(15m, body * 20m)
                + Math.Min(10m, DirectionalCloseStrength(close, context.Direction) * 10m)
                + Math.Min(10m, relativeVolume / context.Settings.V6RangeConfirmationMinRelativeVolume * 8m)
                + Math.Min(10m, Math.Max(0m, roomR) / 1.5m * 10m));

            if (quality < context.Settings.V6MinSetupQuality || roomR <= 0m)
            {
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.RangeRejectionWeak,
                    $"Rectangle fade có event nhưng quality={quality}/100, room TP1={roomR:N2}R.",
                    SetupType.RectangleRangeFade,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    quality);
            }

            return new SetupTriggerDecision(
                true,
                SetupType.RectangleRangeFade,
                SetupTriggerState.Confirmed,
                $"Rectangle fade xác nhận: sweep {boundary:N2}, quality={quality}, " +
                $"body={body:N2}, relativeVolume={relativeVolume:N2}, room TP1={roomR:N2}R.",
                SuggestedLimitEntry: boundary,
                Stage: SetupFunnelStage.Confirmed,
                EventId: eventId,
                SetupQualityScore: quality,
                SuggestedStopLoss: stop,
                SuggestedFirstTakeProfit: firstTarget,
                SuggestedRunnerTakeProfit: runner);
        }

        if (latestPattern is not null)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RangeNotSwept,
                $"Có Rectangle quality={latestPattern.GeometryQuality} nhưng chưa sweep biên theo chiều {context.Direction}.",
                SetupType.RectangleRangeFade,
                SetupFunnelStage.StructureCandidate,
                $"{context.Symbol}:{latestPattern.EventKey}:Fade:{context.Direction}",
                latestPattern.GeometryQuality);
        }

        return SetupTriggerDecision.Reject(
            SetupTriggerState.RangeGeometryWeak,
            "Ngày Range nhưng 32 nến M15 chưa tạo Rectangle đủ touches/containment/độ ổn định.",
            SetupType.RectangleRangeFade,
            SetupFunnelStage.EligibleContext);
    }

    private SetupTriggerDecision EvaluateCompressionBreakout(ScoringContext context, decimal atr)
    {
        var candles = context.EntryCandles;
        var currentIndex = candles.Count - 1;
        SidewaysPattern? latestPattern = null;

        for (var breakIndex = currentIndex;
             breakIndex >= Math.Max(context.Settings.V6PatternLookbackBars,
                 currentIndex - context.Settings.V6BreakoutFreshBars);
             breakIndex--)
        {
            var pattern = _sideways.Detect(candles, breakIndex, context.Settings, atr);
            if (pattern is null) continue;
            latestPattern ??= pattern;

            var boundary = context.Direction == TradeDirection.Long
                ? pattern.UpperAtEnd
                : pattern.FloorAtEnd;
            var buffer = atr * context.Settings.V6BreakoutBufferAtr;
            var breakCandle = candles[breakIndex];
            var previous = candles[breakIndex - 1];
            var crossed = context.Direction == TradeDirection.Long
                ? breakCandle.Close > boundary + buffer && previous.Close <= boundary + buffer
                : breakCandle.Close < boundary - buffer && previous.Close >= boundary - buffer;
            if (!crossed) continue;

            var setupType = pattern.Kind == SidewaysPatternKind.Triangle
                ? SetupType.TriangleBreakout
                : SetupType.RectangleBreakout;
            var eventId = $"{context.Symbol}:{pattern.EventKey}:Break:{context.Direction}";
            var impulseBody = BodyRatio(breakCandle);
            var impulseVolume = RelativeVolume(candles, breakIndex);
            var impulseStrong = IsDirectionalBody(breakCandle, context.Direction)
                                && impulseBody >= context.Settings.MinCandleBodyRatio
                                && impulseVolume >= context.Settings.V6BreakoutMinRelativeVolume
                                && DirectionalCloseStrength(CloseLocation(breakCandle), context.Direction) >= 0.75m;
            if (!impulseStrong)
            {
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.BreakoutWeak,
                    $"{pattern.Kind} breakout nhưng impulse yếu: body={impulseBody:N2}, volume={impulseVolume:N2}.",
                    setupType,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    pattern.GeometryQuality);
            }

            var isDirect = breakIndex == currentIndex;
            if (!isDirect)
            {
                var firstRetest = -1;
                for (var i = breakIndex + 1; i <= currentIndex; i++)
                {
                    if (IsBreakoutRetest(candles[i], context.Direction, boundary, atr, context.Settings))
                    {
                        firstRetest = i;
                        break;
                    }
                }

                if (firstRetest != currentIndex)
                {
                    return SetupTriggerDecision.Reject(
                        SetupTriggerState.BreakoutRetestMissing,
                        firstRetest >= 0
                            ? "Breakout–retest đã xác nhận ở nến trước; không phát lại event."
                            : $"{pattern.Kind} đã breakout nhưng chưa retest/reclaim biên {boundary:N2}.",
                        setupType,
                        SetupFunnelStage.TriggerStarted,
                        eventId,
                        pattern.GeometryQuality);
                }
            }

            var entry = candles[currentIndex].Close;
            var stopBuffer = Math.Max(atr * 0.25m, pattern.EndWidth * 0.10m);
            var stop = context.Direction == TradeDirection.Long
                ? boundary - stopBuffer
                : boundary + stopBuffer;
            var risk = Math.Abs(entry - stop);
            if (risk <= 0m)
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.BreakoutWeak,
                    "Compression breakout không dựng được stop hợp lệ.",
                    setupType,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    pattern.GeometryQuality);

            var firstTarget = context.Direction == TradeDirection.Long
                ? entry + risk * 1.20m
                : entry - risk * 1.20m;
            var measured = context.Direction == TradeDirection.Long
                ? entry + pattern.InitialWidth
                : entry - pattern.InitialWidth;
            var runner = context.Direction == TradeDirection.Long
                ? Math.Max(measured, firstTarget)
                : Math.Min(measured, firstTarget);
            var current = candles[currentIndex];
            var currentBody = BodyRatio(current);
            var currentCloseStrength = DirectionalCloseStrength(CloseLocation(current), context.Direction);
            var quality = ClampScore(
                pattern.GeometryQuality * 0.45m
                + Math.Min(15m, impulseBody * 20m)
                + Math.Min(15m, impulseVolume / context.Settings.V6BreakoutMinRelativeVolume * 10m)
                + Math.Min(10m, currentCloseStrength * 10m)
                + (isDirect ? 5m : 15m));

            if (quality < context.Settings.V6MinSetupQuality)
            {
                return SetupTriggerDecision.Reject(
                    SetupTriggerState.BreakoutWeak,
                    $"{pattern.Kind} breakout có thật nhưng quality={quality}/100 dưới ngưỡng.",
                    setupType,
                    SetupFunnelStage.TriggerStarted,
                    eventId,
                    quality);
            }

            return new SetupTriggerDecision(
                true,
                setupType,
                SetupTriggerState.Confirmed,
                $"{setupType} {(isDirect ? "direct" : "retest")} xác nhận: quality={quality}, " +
                $"impulse body={impulseBody:N2}, volume={impulseVolume:N2}, current body={currentBody:N2}.",
                SuggestedLimitEntry: boundary,
                Stage: SetupFunnelStage.Confirmed,
                EventId: eventId,
                SetupQualityScore: quality,
                SuggestedStopLoss: stop,
                SuggestedFirstTakeProfit: firstTarget,
                SuggestedRunnerTakeProfit: runner);
        }

        if (latestPattern is not null)
        {
            var setup = latestPattern.Kind == SidewaysPatternKind.Triangle
                ? SetupType.TriangleBreakout
                : SetupType.RectangleBreakout;
            return SetupTriggerDecision.Reject(
                SetupTriggerState.BreakoutMissing,
                $"Có {latestPattern.Kind} quality={latestPattern.GeometryQuality} nhưng chưa breakout theo chiều {context.Direction}.",
                setup,
                SetupFunnelStage.StructureCandidate,
                $"{context.Symbol}:{latestPattern.EventKey}:Break:{context.Direction}",
                latestPattern.GeometryQuality);
        }

        return SetupTriggerDecision.Reject(
            SetupTriggerState.CompressionMissing,
            "Chưa có Rectangle/Triangle đủ hình học để arm compression breakout.",
            SetupType.None,
            SetupFunnelStage.EligibleContext);
    }

    private static SetupTriggerDecision EvaluateRange(ScoringContext context, RangeLocation? range)
    {
        if (range is null)
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RangeNotSwept,
                "Không dựng được active range nên không thể xác nhận sweep/rejection.");

        var candle = context.EntryCandles[^1];
        var averageVolume = context.EntryCandles
            .TakeLast(VolumeLookbackBars + 1)
            .SkipLast(1)
            .Average(c => c.Volume);
        var relativeVolume = averageVolume <= 0m ? 0m : candle.Volume / averageVolume;
        var bodyRatio = BodyRatio(candle);
        var closeLocation = CloseLocation(candle);

        var swept = context.Direction == TradeDirection.Long
            ? candle.Low <= range.Low && candle.Close > range.Low
            : candle.High >= range.High && candle.Close < range.High;

        if (!swept)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RangeNotSwept,
                $"Giá ở biên nhưng nến cuối chưa sweep rồi đóng lại trong range [{range.Low:N2}–{range.High:N2}].");
        }

        var directional = context.Direction == TradeDirection.Long
            ? candle.Close > candle.Open && closeLocation >= 1m - ReclaimCloseZone
            : candle.Close < candle.Open && closeLocation <= ReclaimCloseZone;

        if (!directional
            || bodyRatio < context.Settings.MinCandleBodyRatio
            || relativeVolume < context.Settings.V3RangeMinRelativeVolume)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RangeRejectionWeak,
                $"Range sweep có nhưng rejection yếu: body={bodyRatio:N2} " +
                $"(cần {context.Settings.MinCandleBodyRatio:N2}), closeLocation={closeLocation:P0}, " +
                $"relativeVolume={relativeVolume:N2} (cần {context.Settings.V3RangeMinRelativeVolume:N2}).");
        }

        var boundary = context.Direction == TradeDirection.Long ? range.Low : range.High;
        return new SetupTriggerDecision(
            true,
            SetupType.RangeRejection,
            SetupTriggerState.Confirmed,
            $"Range rejection xác nhận tại {boundary:N2}: sweep + close-back, body={bodyRatio:N2}, " +
            $"relativeVolume={relativeVolume:N2}.",
            boundary,
            Stage: SetupFunnelStage.Confirmed,
            SetupQualityScore: 70);
    }

    private SetupTriggerDecision EvaluateTrend(ScoringContext context, DayStructure structure)
    {
        // So với CẤU TRÚC ngày, không với nhãn ngày — nếu không thì một ngày vừa có tin vừa đang
        // trong xu hướng sẽ không bao giờ qua được dòng này. Nhánh Range đã tách ở Evaluate, nên
        // tới đây chỉ còn TrendUp/TrendDown và câu hỏi duy nhất là chiều lệnh có thuận không.
        if (!DayPlaybook.IsTrendAligned(structure, context.Direction))
            return SetupTriggerDecision.Reject(
                SetupTriggerState.NoBreakOfStructure,
                $"Cấu trúc ngày {structure} không thuận chiều {context.Direction} " +
                $"(nhãn ngày {context.DailyPlan.DayRegime}).");

        var atr = AverageTrueRange(context.EntryCandles, 14);
        if (atr <= 0m)
            return SetupTriggerDecision.Reject(SetupTriggerState.ImpulseWeak, "Không tính được ATR cho trigger V3.");

        var result = _structure.Analyze(
            context.EntryCandles,
            context.Settings.SwingPivotBars,
            context.Settings.RetestWindowBars,
            atr);
        var wanted = context.Direction == TradeDirection.Long
            ? StructureBreak.BullishBreak
            : StructureBreak.BearishBreak;

        if (result.Break == StructureBreak.None || result.BrokenLevel is null || result.BreakIndex is null)
            return SetupTriggerDecision.Reject(SetupTriggerState.NoBreakOfStructure, "Chưa có BOS thuận chiều.");

        if (result.Break != wanted)
            return SetupTriggerDecision.Reject(SetupTriggerState.NoBreakOfStructure, "BOS gần nhất đi ngược chiều setup.");

        if (result.RetestFailed)
            return SetupTriggerDecision.Reject(SetupTriggerState.RetestFailed, "BOS đã có nhưng retest đóng thủng invalidation.");

        if (!result.RetestConfirmed || result.RetestIndex is null)
            return SetupTriggerDecision.Reject(SetupTriggerState.BreakUnretested, "BOS chưa có retest/reclaim xác nhận.");

        var barsSinceRetest = context.EntryCandles.Count - 1 - result.RetestIndex.Value;
        if (barsSinceRetest > context.Settings.V3TriggerFreshBars)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.RetestStale,
                $"Retest đã cũ {barsSinceRetest} nến, vượt TTL {context.Settings.V3TriggerFreshBars} nến.");
        }

        var breakCandle = context.EntryCandles[result.BreakIndex.Value];
        var volumeStart = Math.Max(0, result.BreakIndex.Value - VolumeLookbackBars);
        var prior = context.EntryCandles.Skip(volumeStart).Take(result.BreakIndex.Value - volumeStart).ToList();
        var averageBeforeBreak = prior.Count == 0 ? 0m : prior.Average(c => c.Volume);
        var impulseVolume = averageBeforeBreak <= 0m ? 0m : breakCandle.Volume / averageBeforeBreak;
        var impulseBody = BodyRatio(breakCandle);
        var impulseDirectional = IsDirectionalBody(breakCandle, context.Direction);

        if (!impulseDirectional
            || impulseBody < context.Settings.MinCandleBodyRatio
            || impulseVolume < context.Settings.V3MinImpulseVolumeMultiple)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.ImpulseWeak,
                $"BOS có nhưng impulse yếu: body={impulseBody:N2}, relativeVolume={impulseVolume:N2} " +
                $"(cần body {context.Settings.MinCandleBodyRatio:N2}, volume {context.Settings.V3MinImpulseVolumeMultiple:N2}).");
        }

        var pullback = context.EntryCandles
            .Skip(result.BreakIndex.Value + 1)
            .Take(Math.Max(0, result.RetestIndex.Value - result.BreakIndex.Value - 1))
            .ToList();
        var pullbackVolume = pullback.Count == 0 ? 0m : pullback.Average(c => c.Volume);
        var pullbackFraction = breakCandle.Volume <= 0m ? decimal.MaxValue : pullbackVolume / breakCandle.Volume;

        if (pullback.Count > 0 && pullbackFraction > context.Settings.V3PullbackVolumeMaxFraction)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.PullbackVolumeExpanded,
                $"Volume pullback bằng {pullbackFraction:P0} impulse, vượt trần " +
                $"{context.Settings.V3PullbackVolumeMaxFraction:P0}.");
        }

        var reclaim = context.EntryCandles[result.RetestIndex.Value];
        var reclaimBody = BodyRatio(reclaim);
        var reclaimClose = CloseLocation(reclaim);
        var reclaimStrong = IsDirectionalBody(reclaim, context.Direction)
            && reclaimBody >= context.Settings.MinCandleBodyRatio
            && (context.Direction == TradeDirection.Long
                ? reclaimClose >= 1m - ReclaimCloseZone
                : reclaimClose <= ReclaimCloseZone);

        if (!reclaimStrong)
        {
            return SetupTriggerDecision.Reject(
                SetupTriggerState.ReclaimWeak,
                $"Retest giữ mức nhưng nến reclaim yếu: body={reclaimBody:N2}, closeLocation={reclaimClose:P0}.");
        }

        var type = impulseVolume >= context.Settings.VolumeBreakoutMultiple
            ? SetupType.StrongTrendBreakout
            : SetupType.TrendPullback;

        return new SetupTriggerDecision(
            true,
            type,
            SetupTriggerState.Confirmed,
            $"{type} xác nhận: BOS {result.BrokenLevel:N2}, retest mới {barsSinceRetest} nến, " +
            $"impulse volume={impulseVolume:N2}, pullback/impulse={pullbackFraction:P0}.",
            result.BrokenLevel,
            Stage: SetupFunnelStage.Confirmed,
            EventId: $"{context.Symbol}:BOS:{context.EntryCandles[result.BreakIndex.Value].CloseTime:O}:{result.BrokenLevel:N8}:{context.Direction}",
            SetupQualityScore: ClampScore(
                Math.Min(35m, impulseBody * 50m)
                + Math.Min(35m, impulseVolume / context.Settings.V3MinImpulseVolumeMultiple * 25m)
                + Math.Min(20m, (1m - Math.Min(1m, pullbackFraction)) * 20m)
                + Math.Min(10m, DirectionalCloseStrength(reclaimClose, context.Direction) * 10m)));
    }

    private static bool IsRangeConfirmation(
        IReadOnlyList<Candle> candles,
        int index,
        TradeDirection direction,
        decimal boundary,
        EngineSetting settings)
    {
        var candle = candles[index];
        var closedInside = direction == TradeDirection.Long
            ? candle.Close > boundary
            : candle.Close < boundary;
        return closedInside
               && IsDirectionalBody(candle, direction)
               && BodyRatio(candle) >= settings.MinCandleBodyRatio
               && DirectionalCloseStrength(CloseLocation(candle), direction) >= 0.75m
               && RelativeVolume(candles, index) >= settings.V6RangeConfirmationMinRelativeVolume;
    }

    private static bool IsBreakoutRetest(
        Candle candle,
        TradeDirection direction,
        decimal boundary,
        decimal atr,
        EngineSetting settings)
    {
        var touched = candle.Low <= boundary + atr * 0.20m
                      && candle.High >= boundary - atr * 0.20m;
        var held = direction == TradeDirection.Long
            ? candle.Close > boundary + atr * settings.V6BreakoutBufferAtr
            : candle.Close < boundary - atr * settings.V6BreakoutBufferAtr;
        return touched
               && held
               && IsDirectionalBody(candle, direction)
               && BodyRatio(candle) >= settings.MinCandleBodyRatio
               && DirectionalCloseStrength(CloseLocation(candle), direction) >= 0.75m;
    }

    private static decimal RelativeVolume(IReadOnlyList<Candle> candles, int index)
    {
        var start = Math.Max(0, index - VolumeLookbackBars);
        var count = index - start;
        if (count <= 0) return 0m;
        var average = candles.Skip(start).Take(count).Average(c => c.Volume);
        return average <= 0m ? 0m : candles[index].Volume / average;
    }

    private static decimal DirectionalCloseStrength(decimal closeLocation, TradeDirection direction) =>
        direction == TradeDirection.Long ? closeLocation : 1m - closeLocation;

    private static int ClampScore(decimal score) => (int)Math.Clamp(
        decimal.Round(score, 0, MidpointRounding.AwayFromZero), 0m, 100m);

    private static bool IsDirectionalBody(Candle candle, TradeDirection direction) =>
        direction == TradeDirection.Long ? candle.Close > candle.Open : candle.Close < candle.Open;

    private static decimal BodyRatio(Candle candle)
    {
        var range = candle.High - candle.Low;
        return range <= 0m ? 0m : Math.Abs(candle.Close - candle.Open) / range;
    }

    private static decimal CloseLocation(Candle candle)
    {
        var range = candle.High - candle.Low;
        return range <= 0m ? 0.5m : (candle.Close - candle.Low) / range;
    }

    private static decimal AverageTrueRange(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count < period + 1) return 0m;
        var ranges = new List<decimal>(period);
        for (var i = candles.Count - period; i < candles.Count; i++)
        {
            var previousClose = candles[i - 1].Close;
            ranges.Add(Math.Max(
                candles[i].High - candles[i].Low,
                Math.Max(Math.Abs(candles[i].High - previousClose), Math.Abs(candles[i].Low - previousClose))));
        }
        return ranges.Average();
    }
}
