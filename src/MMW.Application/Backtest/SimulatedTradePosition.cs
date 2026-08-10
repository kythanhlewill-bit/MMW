using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Backtest;

/// <summary>
/// Vòng đời một setup mô phỏng. Nhiều điểm vào vẫn là một setup và cùng chia sẻ một ngân
/// sách rủi ro <see cref="SizeR"/>; không tranche nào được cộng thêm rủi ro ngoài ngân sách đó.
/// </summary>
/// <remarks>
/// Toàn bộ số học ở đây quy về MỘT đơn vị duy nhất: R. Mỗi tranche giữ một
/// <see cref="SimulatedEntryTranche.Quantity"/> có đơn vị R trên một đơn vị giá, tính đúng một
/// lần lúc lập vị thế:
///
/// <code>
/// Quantity[i] = SizeR × RiskWeight[i] / |PlannedPrice[i] − InitialStop|
/// </code>
///
/// Nhờ vậy mọi phép quy đổi sau đó chỉ là phép nhân và luôn ra R:
/// lãi/lỗ = <c>move × Quantity</c>, phí = <c>price × feePct × Quantity</c>,
/// phí vốn = <c>markPrice × rate × Quantity</c>.
///
/// Bất biến quan trọng nhất: khớp đủ mọi tranche rồi dừng lỗ tại <see cref="InitialStop"/> mất
/// đúng <see cref="SizeR"/>, không hơn không kém. Khớp một phần thì chỉ mất phần ngân sách của
/// các tranche đã khớp — đúng bản chất của scale-in, và quan trọng hơn là ĐO ĐƯỢC.
/// </remarks>
public sealed class SimulatedTradePosition
{
    /// <summary>
    /// Khoảng cách tối thiểu từ một tranche tới dừng lỗ, theo tỉ lệ của <see cref="UnitRisk"/>.
    /// </summary>
    /// <remarks>
    /// Số lượng tỉ lệ NGHỊCH với khoảng cách tới stop, nên một tranche đặt sát stop sinh khối
    /// lượng khổng lồ: cách stop 0,1 × UnitRisk là gấp mười lần đòn bẩy và gấp mười lần phí tính
    /// theo R của tranche đầu. Đây là loại lỗi không bao giờ tự lộ ra trong báo cáo — nó chỉ làm
    /// một vài lệnh có kết quả kỳ lạ. Chặn ngay tại cổng vào.
    /// </remarks>
    private const decimal MinTrancheStopDistanceRatio = 0.25m;

    private readonly List<SimulatedEntryTranche> _entries;
    private decimal _weightedExitPrice;
    private decimal _weightedPlannedExit;
    private decimal _closedWeight;
    private readonly int _limitEntryExpiryBars;
    private readonly int _trailRunnerPivotBars;
    private readonly List<Candle> _trailCandles = new();

    private SimulatedTradePosition(
        string symbol,
        TradeDirection direction,
        DateTime openedAtUtc,
        decimal sizeR,
        DayRegime regime,
        TradeExecutionPlan plan,
        EngineSetting setting,
        decimal plannedSizeRBeforeDiscipline)
    {
        Symbol = symbol;
        Direction = direction;
        OrderPlacedAtUtc = openedAtUtc;
        OpenedAtUtc = openedAtUtc;
        SizeR = sizeR;
        PlannedSizeRBeforeDiscipline = plannedSizeRBeforeDiscipline;
        Regime = regime;
        Mode = plan.Mode;
        InitialStop = plan.StopLoss;
        Stop = plan.StopLoss;
        FirstTarget = plan.FirstTakeProfit;
        RunnerTarget = plan.RunnerTakeProfit;
        FirstTargetFraction = plan.FirstTakeProfitFraction;
        MoveRunnerStopToBreakeven = plan.MoveRunnerStopToBreakeven;
        PlannedEntry = plan.Entries[0].Price;
        UnitRisk = Math.Abs(PlannedEntry - InitialStop);
        ReferenceQuantity = sizeR / UnitRisk;
        _limitEntryExpiryBars = plan.LimitEntryExpiryBars ?? setting.LimitEntryExpiryBars;
        _trailRunnerPivotBars = plan.TrailRunnerPivotBars;

        // Khối lượng chốt theo giá DỰ KIẾN, không theo giá khớp. Đó là điều chạy thật làm: lệnh
        // được tính khối lượng trước khi gửi đi. Trượt giá sau đó hiện ra thành khoản lỗ lớn hơn
        // ngân sách một chút — đúng như ngoài đời, và không được giấu bằng cách tính lại khối lượng.
        _entries = plan.Entries
            .Select(e => new SimulatedEntryTranche(
                e.Price,
                e.RiskWeight,
                stopDistance: Math.Abs(e.Price - InitialStop),
                quantity: sizeR * e.RiskWeight / Math.Abs(e.Price - InitialStop),
                isLimit: e.IsLimit))
            .ToList();

        // Range/Standard V2 có thể bắt đầu bằng lệnh chờ. Khi đó đây mới là một setup đang chờ,
        // chưa phải vị thế: không tính phí, funding, quota lệnh hay exposure cho tới khi khớp.
        if (!_entries[0].IsLimit) Fill(_entries[0], setting);
    }

    public string Symbol { get; }
    public TradeDirection Direction { get; }
    public DateTime OrderPlacedAtUtc { get; }
    public DateTime OpenedAtUtc { get; private set; }
    public decimal SizeR { get; }

    /// <summary>
    /// Ngân sách dự kiến trước các gate kỷ luật. Oversize phải so với đại lượng này, không được
    /// lấy size đã bị chính gate giảm làm mốc rồi tự co tiếp ở các lệnh sau.
    /// </summary>
    public decimal PlannedSizeRBeforeDiscipline { get; }
    public DayRegime Regime { get; }
    public string Mode { get; }
    public decimal PlannedEntry { get; }
    public decimal InitialStop { get; }
    public decimal Stop { get; private set; }
    public decimal FirstTarget { get; }
    public decimal? RunnerTarget { get; }
    public decimal FirstTargetFraction { get; }
    public bool MoveRunnerStopToBreakeven { get; }
    public decimal UnitRisk { get; }

    /// <summary>Khối lượng của một lệnh MỘT điểm vào cùng ngân sách — mốc quy chiếu của phí %.</summary>
    public decimal ReferenceQuantity { get; }

    public bool FirstTargetTaken { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public TradeOutcome? Outcome { get; private set; }
    public BacktestExitReason? ExitReason { get; private set; }
    public decimal RealizedR { get; private set; }

    /// <summary>Mức giá đã đi thuận chiều xa nhất, quy theo UnitRisk của chân đầu.</summary>
    public decimal MaxFavorableExcursionR { get; private set; }

    /// <summary>Mức giá đã đi ngược chiều xa nhất, quy theo UnitRisk của chân đầu.</summary>
    public decimal MaxAdverseExcursionR { get; private set; }

    public int? BarsToMaxFavorableExcursion { get; private set; }
    public int? BarsToMaxAdverseExcursion { get; private set; }

    /// <summary>
    /// Phí giao dịch tính bằng % khối lượng danh nghĩa, quy về mốc một-điểm-vào.
    /// </summary>
    /// <remarks>
    /// Một lệnh scale-in giao dịch NHIỀU khối lượng hơn lệnh một điểm vào cùng ngân sách rủi ro,
    /// vì các tranche sau nằm gần stop hơn. Với ba tranche cách nhau 0,25R, tổng khối lượng là
    /// 1,44 lần — và hoá đơn phí cũng vậy. Trọng số theo khối lượng thật để con số này không nói dối.
    /// </remarks>
    public decimal FeePercent { get; private set; }

    /// <summary>Tổng phí giao dịch đã trừ khỏi <see cref="RealizedR"/>, tính bằng R.</summary>
    public decimal FeeR { get; private set; }

    /// <summary>Phần phí trả theo biểu maker (chân limit) và theo biểu taker (chân thị trường).</summary>
    public decimal MakerFeeR { get; private set; }
    public decimal TakerFeeR { get; private set; }

    /// <summary>Số lần khớp theo từng biểu phí — tỉ lệ maker/taker của lệnh này.</summary>
    public int MakerFills { get; private set; }
    public int TakerFills { get; private set; }

    /// <summary>Số nến đã trôi qua kể từ khi mở vị thế — đồng hồ của lệnh limit chờ.</summary>
    public int BarsSinceOpen { get; private set; }
    public int BarsSinceFirstFill { get; private set; }

    /// <summary>Số chân limit được đặt ra, số khớp được, và số hết hạn mà chưa khớp.</summary>
    public int LimitTranchesOffered => _entries.Count(e => e.IsLimit);
    public int LimitTranchesFilled => _entries.Count(e => e.IsLimit && e.IsFilled);
    public int LimitTranchesExpired => _entries.Count(e => e.IsExpired);

    /// <summary>
    /// Tổng phí vốn đã trừ khỏi <see cref="RealizedR"/>, tính bằng R. Dương = tiền ra.
    /// </summary>
    public decimal FundingR { get; private set; }

    /// <summary>Số mốc thanh toán phí vốn mà vị thế này đi qua.</summary>
    public int FundingSettlements { get; private set; }

    /// <summary>Trượt giá quy ra R — thứ duy nhất so sánh được giữa các mã có giá khác nhau.</summary>
    public decimal SlippageR { get; private set; }

    public decimal TotalSlippage { get; private set; }
    public IReadOnlyList<SimulatedEntryTranche> Entries => _entries;

    public decimal Entry => WeightedEntry();
    public decimal? ExitPrice => _closedWeight > 0m ? _weightedExitPrice / _closedWeight : null;
    public decimal PlannedExit => _closedWeight > 0m ? _weightedPlannedExit / _closedWeight : 0m;

    /// <summary>Ngân sách rủi ro thực sự đã khớp, trước phí và trượt giá.</summary>
    public decimal FilledRiskBudgetR => SizeR * _entries.Where(e => e.IsFilled).Sum(e => e.RiskWeight);

    /// <summary>
    /// Kết quả chuẩn hoá theo rủi ro đã khớp. Size 0,25R và 1R của cùng một diễn biến giá phải
    /// có cùng R-multiple; size chỉ tác động đường vốn, không được làm sai expectancy tín hiệu.
    /// </summary>
    public decimal RMultiple => FilledRiskBudgetR > 0m ? RealizedR / FilledRiskBudgetR : 0m;
    public bool IsClosed => ClosedAtUtc is not null;
    public bool HasAnyFill => _entries.Any(e => e.IsFilled);
    public bool CancelledWithoutFill { get; private set; }

    public static SimulatedTradePosition Open(
        string symbol,
        TradeDirection direction,
        DateTime openedAtUtc,
        decimal sizeR,
        DayRegime regime,
        TradeExecutionPlan plan,
        EngineSetting setting,
        decimal? plannedSizeRBeforeDiscipline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(setting);

        if (sizeR <= 0m) throw new ArgumentOutOfRangeException(nameof(sizeR));
        if (plan.Entries.Count == 0) throw new ArgumentException("Kế hoạch phải có ít nhất một điểm vào.", nameof(plan));
        if (plan.Entries.Any(e => e.RiskWeight <= 0m)
            || plan.Entries.Sum(e => e.RiskWeight) != 1m)
            throw new ArgumentException(
                "Trọng số RỦI RO của các điểm vào phải dương và có tổng đúng bằng 1.", nameof(plan));
        if (plan.FirstTakeProfitFraction is <= 0m or > 1m)
            throw new ArgumentException("Tỷ trọng chốt lần đầu phải thuộc (0, 1].", nameof(plan));
        if (plan.FirstTakeProfitFraction < 1m && plan.RunnerTakeProfit is null)
            throw new ArgumentException("Kế hoạch chốt một phần phải có mục tiêu runner.", nameof(plan));

        var unitRisk = Math.Abs(plan.Entries[0].Price - plan.StopLoss);
        if (unitRisk <= 0m) throw new ArgumentException("Khoảng entry-stop phải lớn hơn 0.", nameof(plan));

        // Mọi tranche phải nằm đúng phía dừng lỗ và đủ xa nó. Sai phía nghĩa là "vào lệnh khi
        // setup đã bị phủ định"; quá gần nghĩa là khối lượng nổ ra vô hạn (xem
        // MinTrancheStopDistanceRatio). Cả hai đều là lỗi lập kế hoạch, không phải tình huống thị trường.
        var floor = MinTrancheStopDistanceRatio * unitRisk;
        foreach (var tranche in plan.Entries)
        {
            var onCorrectSide = direction == TradeDirection.Long
                ? tranche.Price > plan.StopLoss
                : tranche.Price < plan.StopLoss;
            if (!onCorrectSide)
                throw new ArgumentException(
                    $"Điểm vào {tranche.Price} nằm sai phía dừng lỗ {plan.StopLoss} cho lệnh " +
                    $"{(direction == TradeDirection.Long ? "mua" : "bán")}.", nameof(plan));

            var distance = Math.Abs(tranche.Price - plan.StopLoss);
            // Decimal vẫn phải làm tròn ở phép cộng stop + 0,25×risk khi đã dùng gần đủ 29 chữ
            // số có nghĩa. Cho dung sai 1e-9×UnitRisk chỉ để điểm ĐÚNG biên không bị đọc thành
            // thấp hơn biên vài 1e-29; một vi phạm kinh tế thật vẫn bị chặn như cũ.
            var precisionTolerance = unitRisk * 0.000000001m;
            if (distance + precisionTolerance < floor)
                throw new ArgumentException(
                    $"Điểm vào {tranche.Price} chỉ cách dừng lỗ {distance} — dưới sàn {floor} " +
                    $"({MinTrancheStopDistanceRatio:P0} của {unitRisk}). Khối lượng tỉ lệ nghịch với " +
                    "khoảng cách này nên tranche sát stop sinh đòn bẩy ngoài ý muốn.", nameof(plan));
        }

        var plannedSize = plannedSizeRBeforeDiscipline ?? sizeR;
        if (plannedSize <= 0m || plannedSize < sizeR)
            throw new ArgumentOutOfRangeException(
                nameof(plannedSizeRBeforeDiscipline),
                "Size trước kỷ luật phải dương và không nhỏ hơn size cuối.");

        return new SimulatedTradePosition(
            symbol, direction, openedAtUtc, sizeR, regime, plan, setting, plannedSize);
    }

    /// <summary>
    /// Cập nhật bằng một nến đã đóng. Nếu nến vừa chạm limit mới vừa chạm target, chỉ ghi nhận
    /// limit; không thể biết target xảy ra trước hay sau nên không được chọn thứ tự có lợi.
    /// Nếu cùng chạm stop và target thì stop luôn thắng.
    /// </summary>
    public bool Advance(Candle candle, EngineSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (IsClosed) return true;

        BarsSinceOpen++;
        if (HasAnyFill) BarsSinceFirstFill++;

        // Lệnh limit hết hạn TRƯỚC khi xét khớp của cây nến này. Một nhịp hồi mất hơn
        // `LimitEntryExpiryBars` nến để chạm mức thì không còn là nhịp hồi, nó là một cú khựng —
        // và khớp lúc đó là gia tăng vị thế đúng lúc động lượng đã tắt.
        if (BarsSinceOpen > _limitEntryExpiryBars) ExpireUnfilledLimits();

        if (!HasAnyFill && _entries.All(e => e.IsCancelled))
        {
            CancelledWithoutFill = true;
            ClosedAtUtc = candle.CloseTime;
            return true;
        }

        var filledThisCandle = false;
        if (!FirstTargetTaken)
        {
            foreach (var entry in _entries.Where(e => !e.IsFilled && !e.IsCancelled))
            {
                if (!LimitFilled(candle, entry.PlannedPrice,
                        restsBelow: Direction == TradeDirection.Long, setting)) continue;

                var wasPending = !HasAnyFill;
                Fill(entry, setting);
                if (wasPending)
                {
                    OpenedAtUtc = candle.CloseTime;
                    BarsSinceFirstFill = 1;
                }
                filledThisCandle = true;
            }
        }

        if (!HasAnyFill) return false;

        UpdateExcursions(candle);
        _trailCandles.Add(candle);
        var trailWindow = _trailRunnerPivotBars * 2 + 1;
        if (trailWindow > 0 && _trailCandles.Count > trailWindow)
            _trailCandles.RemoveAt(0);

        // Dừng lỗ là lệnh stop-market: CHẠM mức là kích hoạt. Không áp quy tắc "phải xuyên qua"
        // như với limit — ở đây không có hàng đợi nào để xếp, giá chạm là khớp bằng mọi giá.
        var hitStop = Direction == TradeDirection.Long ? candle.Low <= Stop : candle.High >= Stop;

        // Chốt lời là lệnh limit chờ sẵn ⟹ chịu đúng quy tắc hàng đợi như limit vào lệnh.
        // Thận trọng ở chân vào mà lạc quan ở chân ra là tự nghiêng kết quả về phía có lợi.
        var activeTarget = FirstTargetTaken ? RunnerTarget : FirstTarget;
        var hitTarget = activeTarget is { } target
            && LimitFilled(candle, target, restsBelow: Direction == TradeDirection.Short, setting);

        if (hitStop)
        {
            var slippedStop = ApplySlippage(Stop, Direction, setting.BacktestStopSlippageBps, opening: false);
            CloseRemaining(
                slippedStop, Stop, candle.CloseTime, setting, isMaker: false, BacktestExitReason.Stop);
            return true;
        }

        if (hitTarget && !filledThisCandle && !FirstTargetTaken && FirstTargetFraction < 1m)
        {
            CloseFraction(FirstTargetFraction, FirstTarget, FirstTarget, setting, isMaker: true);
            FirstTargetTaken = true;
            CancelUnfilled();

            if (MoveRunnerStopToBreakeven)
                Stop = FeeAdjustedBreakeven(setting);

            return false;
        }

        if (hitTarget && !filledThisCandle)
        {
            CloseRemaining(
                activeTarget!.Value,
                activeTarget.Value,
                candle.CloseTime,
                setting,
                isMaker: true,
                BacktestExitReason.Target);
            return true;
        }

        // Dừng theo thời gian chỉ chạy SAU stop/target. Nếu cùng một nến chạm mục tiêu thì không
        // được biến lệnh thắng thành time-stop chỉ vì nó cũng vừa hết 16 nến.
        if (BarsSinceFirstFill >= setting.TimeStopBars
            && MaxFavorableExcursionR < setting.TimeStopMinR)
        {
            var exit = ApplySlippage(
                candle.Close, Direction, setting.BacktestStopSlippageBps, opening: false);
            CloseRemaining(
                exit,
                candle.Close,
                candle.CloseTime,
                setting,
                isMaker: false,
                BacktestExitReason.TimeStop);
            return true;
        }

        UpdateTrailingStopForNextCandle(candle);

        return false;
    }

    /// <summary>Nến này có khớp một lệnh limit chờ tại <paramref name="price"/> không.</summary>
    /// <param name="restsBelow">Lệnh nằm DƯỚI giá thị trường (mua limit / chốt lời của lệnh bán).</param>
    private static bool LimitFilled(
        Candle candle, decimal price, bool restsBelow, EngineSetting setting) =>
        restsBelow
            ? (setting.BacktestLimitFillRequiresThrough ? candle.Low < price : candle.Low <= price)
            : (setting.BacktestLimitFillRequiresThrough ? candle.High > price : candle.High >= price);

    private void ExpireUnfilledLimits()
    {
        foreach (var entry in _entries.Where(e => !e.IsFilled && !e.IsCancelled && e.IsLimit))
        {
            entry.IsCancelled = true;
            entry.IsExpired = true;
        }
    }

    /// <summary>Đóng vị thế còn lại tại giá cuối kỳ để báo cáo không loại thiên lệch lệnh đang mở.</summary>
    public void CloseAtMarket(Candle candle, EngineSetting setting)
    {
        if (IsClosed) return;

        if (!HasAnyFill)
        {
            ExpireUnfilledLimits();
            CancelledWithoutFill = true;
            ClosedAtUtc = candle.CloseTime;
            return;
        }

        // Đóng cưỡng bức cuối kỳ là lệnh thị trường ⟹ taker kèm trượt giá, như mọi lần bấm nút thật.
        var exit = ApplySlippage(candle.Close, Direction, setting.BacktestStopSlippageBps, opening: false);
        CloseRemaining(
            exit,
            candle.Close,
            candle.CloseTime,
            setting,
            isMaker: false,
            BacktestExitReason.EndOfPeriod);
    }

    /// <summary>
    /// Khớp một chân vào lệnh. Loại lệnh quyết định cả phí lẫn trượt giá — không phải ước lượng.
    /// </summary>
    /// <remarks>
    /// Lệnh limit đang CHỜ SẴN trong sổ: nó khớp đúng tại mức đã đặt hoặc tốt hơn, không bao giờ
    /// tệ hơn. Áp trượt giá bất lợi cho nó — như V1 làm với mọi chân — là phạt một chuyện về
    /// nguyên tắc không xảy ra, và tệ hơn: phạt đúng cái cải tiến mà V2 đang cần đo.
    ///
    /// Cái giá thật của lệnh limit không phải trượt giá mà là RỦI RO KHÔNG KHỚP. Cái giá đó đã
    /// được tính ở chỗ khác, bằng quy tắc "phải xuyên qua" và bằng hết hạn theo số nến.
    /// </remarks>
    private void Fill(SimulatedEntryTranche entry, EngineSetting setting)
    {
        entry.EntryPrice = entry.IsLimit
            ? entry.PlannedPrice
            : ApplySlippage(entry.PlannedPrice, Direction, setting.BacktestEntrySlippageBps, opening: true);
        entry.IsFilled = true;

        ChargeFee(entry.EntryPrice.Value, entry.Quantity, setting, isMaker: entry.IsLimit);

        // Trượt giá vào lệnh KHÔNG bị trừ riêng: nó đã nằm sẵn trong giá khớp, nên khoản lỗ/lãi
        // khi thoát đã phản ánh đủ. Ghi lại chỉ để chẩn đoán.
        var slip = Math.Abs(entry.EntryPrice.Value - entry.PlannedPrice);
        SlippageR += slip * entry.Quantity;
        TotalSlippage += slip * entry.RiskWeight;
    }

    /// <summary>
    /// Thanh toán phí vốn tại một mốc funding mà vị thế còn mở. Tỷ lệ dương ⟹ Long TRẢ, Short NHẬN.
    /// </summary>
    /// <remarks>
    /// Chỉ tính trên phần khối lượng ĐANG mở: tranche chưa khớp không nắm giữ gì, tranche đã chốt
    /// một nửa chỉ còn nửa kia phải trả.
    ///
    /// Với dừng lỗ khoảng 0,27% giá, một mốc thanh toán 0,01% tốn ~0,037R. Nghe nhỏ, nhưng
    /// baseline V1.4 có expectancy −0,04R — nghĩa là phí vốn cùng bậc độ lớn với toàn bộ khoảng
    /// cách từ chiến lược tới hoà vốn. Bỏ qua nó là bỏ qua đúng thứ đang quyết định kết quả.
    /// </remarks>
    public void SettleFunding(decimal fundingRate, decimal markPrice)
    {
        if (IsClosed || markPrice <= 0m) return;

        var openQuantity = _entries
            .Where(e => e.IsFilled && e.RemainingFraction > 0m)
            .Sum(e => e.Quantity * e.RemainingFraction);
        if (openQuantity <= 0m) return;

        FundingSettlements++;
        if (fundingRate == 0m) return;

        var cost = markPrice * fundingRate * openQuantity;
        if (Direction == TradeDirection.Short) cost = -cost;

        FundingR += cost;
        RealizedR -= cost;
    }

    private void CloseFraction(
        decimal fractionOfEachTranche, decimal exit, decimal plannedExit, EngineSetting setting, bool isMaker)
    {
        foreach (var entry in _entries.Where(e => e.IsFilled && e.RemainingFraction > 0m))
        {
            var trancheFraction = Math.Min(entry.RemainingFraction, fractionOfEachTranche);
            CloseTranche(entry, trancheFraction, exit, plannedExit, setting, isMaker);
        }
    }

    private void CloseRemaining(
        decimal exit,
        decimal plannedExit,
        DateTime closedAtUtc,
        EngineSetting setting,
        bool isMaker,
        BacktestExitReason exitReason)
    {
        foreach (var entry in _entries.Where(e => e.IsFilled && e.RemainingFraction > 0m))
            CloseTranche(entry, entry.RemainingFraction, exit, plannedExit, setting, isMaker);

        CancelUnfilled();
        ClosedAtUtc = closedAtUtc;
        ExitReason = exitReason;
        Outcome = RealizedR > 0m
            ? TradeOutcome.Win
            : RealizedR < 0m ? TradeOutcome.Loss : TradeOutcome.BreakEven;
    }

    private void CloseTranche(
        SimulatedEntryTranche entry,
        decimal fractionOfTranche,
        decimal exit,
        decimal plannedExit,
        EngineSetting setting,
        bool isMaker)
    {
        if (fractionOfTranche <= 0m || entry.EntryPrice is not { } actualEntry) return;

        // Khối lượng RIÊNG của tranche, không phải trọng số rủi ro chia cho UnitRisk chung.
        // Thoát tại InitialStop cho `move = −stopDistance`, nên đóng góp đúng bằng −SizeR × RiskWeight.
        var closedQuantity = entry.Quantity * fractionOfTranche;
        var move = Direction == TradeDirection.Long ? exit - actualEntry : actualEntry - exit;
        RealizedR += move * closedQuantity;
        entry.RemainingFraction -= fractionOfTranche;

        ChargeFee(exit, closedQuantity, setting, isMaker);

        var slip = Math.Abs(exit - plannedExit);
        SlippageR += slip * closedQuantity;
        TotalSlippage += slip * entry.RiskWeight * fractionOfTranche;

        _weightedExitPrice += exit * closedQuantity;
        _weightedPlannedExit += plannedExit * closedQuantity;
        _closedWeight += closedQuantity;
    }

    private void ChargeFee(decimal price, decimal quantity, EngineSetting setting, bool isMaker)
    {
        var rate = isMaker ? setting.BacktestMakerFeePercent : setting.BacktestTakerFeePercent;
        var fee = price * (rate / 100m) * quantity;

        FeeR += fee;
        RealizedR -= fee;
        FeePercent += rate * quantity / ReferenceQuantity;

        if (isMaker) { MakerFeeR += fee; MakerFills++; }
        else { TakerFeeR += fee; TakerFills++; }
    }

    private void CancelUnfilled()
    {
        foreach (var entry in _entries.Where(e => !e.IsFilled)) entry.IsCancelled = true;
    }

    /// <summary>Giá vào bình quân theo KHỐI LƯỢNG — mốc hoà vốn thật của vị thế gộp.</summary>
    /// <remarks>
    /// Bình quân theo trọng số rủi ro sẽ lệch, vì tranche vào sâu nắm nhiều hợp đồng hơn nên kéo
    /// giá vốn về phía nó mạnh hơn phần rủi ro nó tiêu.
    ///
    /// ⚠️ Đây là hoà vốn TRƯỚC phí. Dời stop về đây vẫn còn lỗ đúng bằng phí hai chiều. Hoà vốn
    /// thật (entry + phí) thuộc §7 và chưa làm.
    /// </remarks>
    private decimal WeightedEntry()
    {
        var filled = _entries.Where(e => e.IsFilled && e.EntryPrice is not null).ToList();
        var quantity = filled.Sum(e => e.Quantity);
        return quantity <= 0m
            ? PlannedEntry
            : filled.Sum(e => e.EntryPrice!.Value * e.Quantity) / quantity;
    }

    /// <summary>
    /// Giá dừng bảo vệ cả phí đã trả, phí taker dự kiến khi stop khớp và đệm 0,05R.
    /// </summary>
    private decimal FeeAdjustedBreakeven(EngineSetting setting)
    {
        var openQuantity = _entries
            .Where(e => e.IsFilled && e.RemainingFraction > 0m)
            .Sum(e => e.Quantity * e.RemainingFraction);
        if (openQuantity <= 0m) return WeightedEntry();

        var entry = WeightedEntry();
        var expectedExitFeeR = entry * (setting.BacktestTakerFeePercent / 100m) * openQuantity;
        var paidCostsPerUnit = (FeeR + FundingR + expectedExitFeeR) / openQuantity;
        var buffer = paidCostsPerUnit + 0.05m * UnitRisk;
        return Direction == TradeDirection.Long ? entry + buffer : entry - buffer;
    }

    private void UpdateExcursions(Candle candle)
    {
        var favorableMove = Direction == TradeDirection.Long
            ? candle.High - PlannedEntry
            : PlannedEntry - candle.Low;
        var favorableR = Math.Max(0m, favorableMove / UnitRisk);
        if (favorableR > MaxFavorableExcursionR)
        {
            MaxFavorableExcursionR = favorableR;
            BarsToMaxFavorableExcursion = BarsSinceFirstFill;
        }

        var adverseMove = Direction == TradeDirection.Long
            ? PlannedEntry - candle.Low
            : candle.High - PlannedEntry;
        var adverseR = Math.Max(0m, adverseMove / UnitRisk);
        if (adverseR > MaxAdverseExcursionR)
        {
            MaxAdverseExcursionR = adverseR;
            BarsToMaxAdverseExcursion = BarsSinceFirstFill;
        }
    }

    /// <summary>
    /// Xác nhận pivot bằng đủ nến hai phía rồi mới siết stop cho cây nến KẾ TIẾP. Không bao giờ
    /// dùng low/high của chính cây nến để giả vờ stop mới đã tồn tại từ đầu cây đó.
    /// </summary>
    private void UpdateTrailingStopForNextCandle(Candle current)
    {
        if (!FirstTargetTaken || _trailRunnerPivotBars <= 0) return;

        var required = _trailRunnerPivotBars * 2 + 1;
        if (_trailCandles.Count < required) return;

        var pivot = _trailCandles[_trailRunnerPivotBars];
        var left = _trailCandles.Take(_trailRunnerPivotBars).ToList();
        var right = _trailCandles.Skip(_trailRunnerPivotBars + 1).ToList();

        if (Direction == TradeDirection.Long)
        {
            var confirmed = left.All(c => pivot.Low < c.Low) && right.All(c => pivot.Low <= c.Low);
            if (confirmed && pivot.Low > Stop && pivot.Low < current.Close) Stop = pivot.Low;
        }
        else
        {
            var confirmed = left.All(c => pivot.High > c.High) && right.All(c => pivot.High >= c.High);
            if (confirmed && pivot.High < Stop && pivot.High > current.Close) Stop = pivot.High;
        }
    }

    /// <summary>Trượt giá luôn theo hướng bất lợi, cả khi vào lẫn khi thoát.</summary>
    private static decimal ApplySlippage(
        decimal price, TradeDirection direction, decimal bps, bool opening)
    {
        var delta = price * bps / 10_000m;
        var worseIsHigher = opening ? direction == TradeDirection.Long : direction == TradeDirection.Short;
        return worseIsHigher ? price + delta : price - delta;
    }
}

public sealed class SimulatedEntryTranche
{
    internal SimulatedEntryTranche(
        decimal plannedPrice, decimal riskWeight, decimal stopDistance, decimal quantity, bool isLimit)
    {
        PlannedPrice = plannedPrice;
        RiskWeight = riskWeight;
        StopDistance = stopDistance;
        Quantity = quantity;
        IsLimit = isLimit;
    }

    public decimal PlannedPrice { get; }

    /// <summary>Lệnh limit chờ sẵn (maker, không trượt giá, có thể không khớp) hay lệnh thị trường.</summary>
    public bool IsLimit { get; }

    /// <summary>Phần ngân sách rủi ro của setup dành cho tranche này.</summary>
    public decimal RiskWeight { get; }

    /// <summary>Khoảng cách từ giá dự kiến tới dừng lỗ BAN ĐẦU — mẫu số sinh ra khối lượng.</summary>
    public decimal StopDistance { get; }

    /// <summary>
    /// Khối lượng, chuẩn hoá sao cho <c>move × Quantity</c> ra thẳng R. Cố định lúc lập vị thế:
    /// dời stop về hoà vốn KHÔNG được tính lại khối lượng, vì hợp đồng đã mua thì đã mua rồi.
    /// </summary>
    public decimal Quantity { get; }
    public decimal? EntryPrice { get; internal set; }
    public decimal RemainingFraction { get; internal set; } = 1m;
    public bool IsFilled { get; internal set; }
    public bool IsCancelled { get; internal set; }

    /// <summary>
    /// Bị huỷ vì HẾT HẠN chờ, khác với bị huỷ vì lệnh đã chốt lời hoặc dừng lỗ.
    /// </summary>
    /// <remarks>
    /// Phân biệt hai lý do huỷ là bắt buộc để đọc được tỉ lệ khớp: huỷ vì TP1/stop nghĩa là kế
    /// hoạch đã xong việc của nó, còn hết hạn nghĩa là mức đặt sai chỗ. Gộp chung sẽ che mất
    /// trường hợp thứ hai — trường hợp duy nhất cần sửa.
    /// </remarks>
    public bool IsExpired { get; internal set; }
}
