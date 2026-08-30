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
    /// <param name="settings">
    /// Cần cho phần chốt hai mục tiêu. Bỏ trống thì kế hoạch quay về một mục tiêu duy nhất —
    /// đúng hành vi của mọi phiên bản trước V7.
    /// </param>
    TradeExecutionPlan? PlanLive(EntryScorecard card, EngineSetting? settings = null);
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

    /// <summary>
    /// Khoảng cách TỐI THIỂU giữa mức chờ và giá thị trường, tính theo khoảng cách tới dừng lỗ.
    /// </summary>
    /// <remarks>
    /// Điều kiện cũ chỉ là "thấp hơn giá hiện tại" — chấp nhận cả chênh lệch bằng 0. Nhưng
    /// <c>SuggestedEntry</c> là giá ticker tại lúc CHẤM, còn lệnh đi ra sàn vài giây sau đó, nên
    /// một mức chờ cách giá vài phần vạn không còn thụ động khi nó tới nơi: sàn từ chối bằng
    /// -5022 (post-only), hoặc tệ hơn — nếu không bật post-only thì nó khớp thành taker trong khi
    /// cổng chi phí đã chấm theo phí maker.
    ///
    /// Đo trên 2.567 phiếu từng có mức chờ: 343 phiếu (13%) đặt dưới 2 phần vạn và 1.334 phiếu
    /// (52%) dưới 10 phần vạn — tức là nằm gọn trong vùng mà riêng chênh lệch mua/bán đã nuốt hết.
    /// Hai lệnh ETHUSDT #37 và #50 bị từ chối thật với khoảng cách 1,3 và 4,7 phần vạn.
    ///
    /// Lấy tỉ lệ theo khoảng cách dừng lỗ chứ không lấy số phần vạn cố định: nó tự co giãn theo
    /// biến động của chính setup, và đó là đơn vị mà phần còn lại của engine đang nói.
    ///
    /// Không đạt ngưỡng thì trả <c>null</c> → kế hoạch lùi về lệnh thị trường. Vì cổng chi phí và
    /// bộ đặt lệnh dùng CHUNG <see cref="PlanLive"/>, cổng sẽ tự chấm lại theo phí taker; lệnh nào
    /// không gánh nổi phí thật thì bị loại ở đó, đúng chỗ nó phải bị loại.
    /// </remarks>
    private const decimal MinPassiveOffsetOfStopDistance = 0.15m;
    private const int StrongStructurePoints = 8;

    /// <summary>Nhãn <see cref="TradeExecutionPlan.Mode"/> của kế hoạch chạy thật.</summary>
    public const string LiveMode = "LiveSingleMarket";

    /// <inheritdoc />
    public TradeExecutionPlan? PlanLive(EntryScorecard card, EngineSetting? settings = null)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Ba mức giá là điều kiện cần của chính bộ đặt lệnh: LiveOrderService chặn thẳng lệnh
        // thiếu dừng lỗ hoặc chốt lời. Trả null thay vì ném để một phiếu thiếu giá không giết
        // cả chu kỳ chấm điểm — và để cổng chi phí lẫn bộ đặt lệnh cùng đọc MỘT câu trả lời cho
        // câu hỏi "phiếu này có chạy thật được không".
        if (card.SuggestedEntry is not { } entry || entry <= 0m) return null;
        if (card.SuggestedStopLoss is not { } stop || stop <= 0m) return null;
        if (card.SuggestedTakeProfit is not { } target || target <= 0m) return null;
        if (card.Direction is not { } direction) return null;
        if (Math.Abs(entry - stop) <= 0m) return null;

        // Vào bằng lệnh chờ khi có một mức THỤ ĐỘNG dùng được, còn không thì lệnh thị trường.
        // Không bịa ra mức chờ: một lệnh chờ đặt sai phía sổ sẽ khớp ngay như lệnh thị trường,
        // và mô hình chi phí lại tính phí maker cho một cú khớp taker — đúng loại nói dối mà
        // `PlanLive` sinh ra để chấm dứt.
        // MaCrossFast BẮT BUỘC là lệnh thị trường. Bộ kích hoạt trả SuggestedLimitEntry = null cho
        // nó, nhưng SignalEvalService lùi về mức chờ dựng từ EMA khi trigger không nêu mức — nên
        // nếu không chặn ở đây, nhánh mua-sự-có-mặt lại thành nhánh chờ giá quay đầu.
        var passive = card.SetupType == SetupType.MaCrossFast
            ? null
            : PassiveLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, settings);

        // Chốt hai phần khi phiếu mang đủ HAI mức khác nhau và chúng xếp đúng thứ tự. Thiếu một
        // trong hai thì quay về một mục tiêu — chứ không bịa ra mức thứ hai bằng một bội R nào
        // đó. Một mục tiêu bịa ra sẽ được cổng chi phí chấm như mục tiêu thật, và mọi con số
        // kỳ vọng sau đó đo một thứ không tồn tại.
        var runner = TwoStageTargets(card, target, direction, settings, out var firstTarget, out var fraction);

        return new TradeExecutionPlan(
            [new PlannedEntryTranche(passive ?? entry, 1m, IsLimit: passive is not null)],
            stop,
            firstTarget,
            RunnerTakeProfit: runner,
            FirstTakeProfitFraction: fraction,
            // Kéo về hoà vốn ngay sau khi chốt phần đầu. Đây là nửa còn lại của lý do chốt từng
            // phần: chốt một nửa mà vẫn để nửa kia rơi về dừng lỗ gốc thì lệnh vẫn lỗ, chỉ lỗ
            // chậm hơn. Chốt một nửa RỒI kéo về hoà vốn mới biến kết cục xấu nhất thành +0,5R.
            MoveRunnerStopToBreakeven: runner is not null,
            Mode: LiveMode,
            TrailRunnerPivotBars: runner is not null ? settings?.V7TrailPivotBars ?? 0 : 0,
            // Lệnh chờ của bộ luật swing phải sống lâu hơn hẳn: nhịp hồi khung 4 giờ mất nhiều
            // giờ để đi hết, còn hạn một giờ được chọn cho setup trong phiên. Đây chính là chỗ
            // mà bảy lệnh bị huỷ vì "quá 60 phút chưa khớp" đã đi qua.
            LimitEntryExpiryBars: passive is null
                ? null
                : settings?.StrategyVersion.UsesHtfSwing() == true
                    ? HtfSwingLimitExpiryBars
                    : LiveLimitExpiryBars);
    }

    /// <summary>
    /// Tách mục tiêu của phiếu thành mục tiêu gần và mục tiêu cuối, hoặc trả <c>null</c> cho
    /// mục tiêu cuối khi phiếu chỉ có một mức dùng được.
    /// </summary>
    private static decimal? TwoStageTargets(
        EntryScorecard card,
        decimal fallbackTarget,
        TradeDirection direction,
        EngineSetting? settings,
        out decimal firstTarget,
        out decimal fraction)
    {
        firstTarget = fallbackTarget;
        fraction = 1m;

        if (settings is null) return null;
        if (card.SuggestedFirstTakeProfit is not { } first || first <= 0m) return null;
        if (card.SuggestedRunnerTakeProfit is not { } runner || runner <= 0m) return null;

        var isLong = direction == TradeDirection.Long;

        // Cả hai phải nằm đúng phía giá vào, và mục tiêu cuối phải xa hơn mục tiêu gần.
        if (card.SuggestedEntry is not { } entry || entry <= 0m) return null;
        if (isLong ? first <= entry || runner <= first : first >= entry || runner >= first) return null;

        var f = settings.V7FirstTargetFraction;
        if (f is <= 0m or >= 1m) return null;

        firstTarget = first;
        fraction = f;
        return runner;
    }

    /// <summary>Số nến chờ trước khi huỷ lệnh chờ chưa khớp của đường chạy thật.</summary>
    /// <remarks>
    /// Bốn nến 15 phút = một giờ. Setup đã được chấm trên một cây nến cụ thể; để lệnh chờ nằm
    /// lâu hơn nghĩa là chấp nhận khớp vào một thị trường đã khác hẳn thị trường lúc chấm điểm.
    /// </remarks>
    public const int LiveLimitExpiryBars = 4;

    /// <summary>
    /// Mức đặt lệnh chờ nằm đúng phía THỤ ĐỘNG của sổ lệnh, hoặc <c>null</c> nếu không có.
    /// </summary>
    /// <remarks>
    /// Phía thụ động là phía mà lệnh KHÔNG khớp ngay: mua thì phải thấp hơn giá hiện tại, bán
    /// thì phải cao hơn. Đặt sai phía, hoặc đặt đúng bằng giá hiện tại, thì lệnh cắt qua sổ và
    /// thành taker.
    ///
    /// Mức chờ cũng phải cách dừng lỗ một quãng: khối lượng = rủi ro / |vào − dừng|, nên một
    /// mức chờ sát dừng lỗ làm khối lượng nổ ra ngoài ý muốn. Dùng chung sàn 25% khoảng rủi ro
    /// với <see cref="SafeLimitEntry"/>, nhưng ở đây LOẠI mức vi phạm thay vì kéo nó về — kéo
    /// về sẽ đẻ ra một mức chờ mà chính bộ chấm điểm chưa từng nhìn thấy.
    /// </remarks>
    private static decimal? PassiveLimitEntry(
        decimal? suggested, decimal entry, decimal stop, TradeDirection direction,
        EngineSetting? settings)
    {
        if (suggested is not { } candidate || candidate <= 0m) return null;

        var stopDistance = Math.Abs(entry - stop);
        var minOffset = stopDistance * MinPassiveOffsetOfStopDistance;

        // Sàn TƯƠNG ĐỐI: mức chờ không được nằm trong 25% cuối của khoảng dừng lỗ.
        //
        // Nhưng một sàn tương đối không bảo vệ được cái nó định bảo vệ. Nó cho phép khoảng dừng
        // lỗ HIỆU DỤNG — đo từ mức chờ chứ không từ giá lúc chấm — co xuống còn 25% khoảng gốc,
        // tức khối lượng gấp 4 và phí theo R cũng gấp 4. Sàn tuyệt đối MinStopDistancePercent
        // được áp trước đó trong bộ kích hoạt sẽ bị gặm mất đúng bằng lượng ấy:
        //
        // <code>
        // Lệnh #52 (BTCUSDT, MaPullback): chấm 40,0 bps → mức chờ kéo còn 29,4 bps
        // Lệnh #65 (ETHUSDT, MaPullback): chấm 40,0 bps → mức chờ kéo còn 25,1 bps
        // </code>
        //
        // Cả hai đều lỗ. Nên sàn ở đây phải là MAX của hai loại: giữ nguyên ràng buộc hình học
        // 25%, đồng thời không cho khoảng hiệu dụng rơi xuống dưới sàn kinh tế tuyệt đối. Không
        // đạt thì trả null → lùi về lệnh thị trường, nơi khoảng dừng lỗ đúng bằng con số đã chấm.
        var relativeFloor = stopDistance * 0.25m;
        var absoluteFloor = settings is null
            ? 0m
            : entry * settings.MinStopDistancePercent / 100m;
        var floor = Math.Max(relativeFloor, absoluteFloor);

        return direction == TradeDirection.Long
            ? candidate <= entry - minOffset && candidate >= stop + floor ? candidate : null
            : candidate >= entry + minOffset && candidate <= stop - floor ? candidate : null;
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

        // Ba setup MA tự mang theo TRỌN BỘ mức giá do bộ kích hoạt tính ra, nên planner chỉ việc
        // hiện thực hoá chúng. Rẽ trước cả nhánh phiên bản: chúng không thuộc họ V3/V6 nào cả,
        // và để rơi xuống dưới thì PlanV3 sẽ ném "chỉ lập lệnh cho setup đã xác nhận".
        if (card.SetupType is SetupType.MaCrossFast or SetupType.MaPullback or SetupType.MaDeepPullback)
            return PlanMa(card, settings, direction, entry, stop);

        // V7 phải rẽ TRƯỚC hai nhánh dưới. Bộ kích hoạt của nó đã tính sẵn cả ba mức từ cấu trúc
        // 4h, nên planner chỉ hiện thực hoá; để rơi xuống PlanV3/PlanV6 thì mục tiêu sẽ bị tính
        // lại theo bội R của khung vào lệnh và toàn bộ phần "mục tiêu đo bằng cấu trúc khung
        // lớn" — lý do tồn tại của bộ luật này — biến mất im lặng.
        if (settings.StrategyVersion.UsesHtfSwing())
            return PlanHtfSwing(card, settings, direction, entry, stop, unitRisk);

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

    /// <summary>
    /// Kế hoạch cho bộ luật swing 4h: một chân vào bằng lệnh chờ, hai mục tiêu, có kéo dừng lỗ.
    /// </summary>
    /// <remarks>
    /// <para>Ba lựa chọn ở đây đều khác hẳn các nhánh trên, và cả ba đều phục vụ cùng một mục
    /// đích: để phần LỚN của lệnh còn sống tới mục tiêu 4h.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Không chia nhiều chân vào.</b> Vùng giá trị đã là một khoảng rồi; chia thêm chân
    /// bên trong một khoảng chỉ làm giá vào trung bình nhích vài điểm cơ bản, đổi lấy rủi ro
    /// không khớp đủ chân trên một setup mỗi tuần chỉ có vài lần.</item>
    /// <item><b>Luôn vào bằng lệnh chờ.</b> Nhịp hồi 4h đi chậm — đây chính là loại setup mà
    /// lệnh chờ khớp được, và phí maker là giả định mà cổng chi phí đang dựa vào.</item>
    /// <item><b>Lệnh chờ sống lâu hơn hẳn.</b> Một nhịp hồi khung 4h mất nhiều giờ để hoàn tất;
    /// hạn một giờ như đường chạy thật của V6 sẽ huỷ lệnh trước cả khi giá kịp tới nơi.</item>
    /// </list>
    /// </remarks>
    private static TradeExecutionPlan PlanHtfSwing(
        EntryScorecard card,
        EngineSetting settings,
        TradeDirection direction,
        decimal entry,
        decimal stop,
        decimal unitRisk)
    {
        var first = card.SuggestedFirstTakeProfit
                    ?? card.SuggestedTakeProfit
                    ?? AtR(entry, unitRisk, direction, settings.V7MinFirstRr);

        var runner = card.SuggestedRunnerTakeProfit;
        var isLong = direction == TradeDirection.Long;

        // Mục tiêu cuối phải xa hơn mục tiêu gần; không thì hai lệnh chốt tranh nhau trên sàn và
        // cái nào khớp trước là do may rủi.
        if (runner is { } r && (isLong ? r <= first : r >= first)) runner = null;

        var limit = SafeLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.10m);

        return new TradeExecutionPlan(
            [new PlannedEntryTranche(limit, 1m, IsLimit: true)],
            stop,
            first,
            RunnerTakeProfit: runner,
            FirstTakeProfitFraction: runner is null ? 1m : settings.V7FirstTargetFraction,
            MoveRunnerStopToBreakeven: runner is not null,
            Mode: "HtfSwing",
            TrailRunnerPivotBars: runner is null ? 0 : settings.V7TrailPivotBars,
            LimitEntryExpiryBars: HtfSwingLimitExpiryBars);
    }

    /// <summary>
    /// Số nến khung vào lệnh mà lệnh chờ của bộ luật swing được sống.
    /// </summary>
    /// <remarks>
    /// 24 nến 15 phút = 6 giờ, tức khoảng một nến rưỡi trên khung 4 giờ. Đó là quãng thời gian
    /// mà một nhịp hồi 4h thật sự cần để đi hết; con số một giờ của các bản trước được chọn cho
    /// setup trong phiên, và áp nó vào đây sẽ huỷ gần hết lệnh chờ trước khi giá kịp tới vùng.
    /// </remarks>
    public const int HtfSwingLimitExpiryBars = 24;

    /// <summary>
    /// Kế hoạch cho ba setup họ MA: một chân, trọn ngân sách rủi ro, dùng thẳng mức của phiếu.
    /// </summary>
    /// <remarks>
    /// KHÔNG chia nhiều chân và KHÔNG tự dựng lại mục tiêu, vì bộ kích hoạt đã quyết cả hai:
    /// dừng lỗ đặt ở giữa đáy xoay và đáy vùng tích luỹ (hoặc dưới MA99), còn mục tiêu là bội R
    /// theo thứ tự nhịp hồi (hoặc chính mức bị từ chối). Dựng lại ở đây sẽ cho ra một lệnh khác
    /// với lệnh mà cổng chi phí đã chấm — đúng loại lệch mà <c>PlanLive</c> sinh ra để chấm dứt.
    ///
    /// Chỉ <see cref="SetupType.MaCrossFast"/> vào bằng lệnh thị trường: giá trị của nó nằm ở
    /// chỗ có mặt ngay lúc xu hướng vừa đổi, mà lệnh chờ thì có thể không khớp.
    /// </remarks>
    private static TradeExecutionPlan PlanMa(
        EntryScorecard card,
        EngineSetting settings,
        TradeDirection direction,
        decimal entry,
        decimal stop)
    {
        var target = card.SuggestedFirstTakeProfit
                     ?? card.SuggestedTakeProfit
                     ?? AtR(entry, Math.Abs(entry - stop), direction, 2m);

        var isMarket = card.SetupType == SetupType.MaCrossFast;
        var price = isMarket
            ? entry
            : SafeLimitEntry(card.SuggestedLimitEntry, entry, stop, direction, fallbackPullbackR: 0.10m);

        return new TradeExecutionPlan(
            [new PlannedEntryTranche(price, 1m, IsLimit: !isMarket)],
            stop,
            target,
            RunnerTakeProfit: null,
            FirstTakeProfitFraction: 1m,
            MoveRunnerStopToBreakeven: false,
            Mode: card.SetupType.ToString(),
            LimitEntryExpiryBars: isMarket ? null : LiveLimitExpiryBars);
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
