using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Abstractions;
using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Services;

public interface IScorecardOutcomeReviewService
{
    /// <summary>Phân giải các phiếu chưa có kết cục và đã đủ nến. Trả về số bản ghi vừa sinh.</summary>
    Task<int> ResolvePendingAsync(DateTime utcNow, int maxCards = 500, CancellationToken ct = default);
}

/// <summary>
/// Cho từng phiếu chấm điểm chạy tiếp trên kho nến để biết cổng veto đã chặn đúng hay sai.
/// </summary>
/// <remarks>
/// <b>Nguyên tắc số một: không viết lại luật khớp lệnh.</b> Toàn bộ phần "chạm stop hay chạm mục
/// tiêu trước, mất bao nhiêu phí" giao cho <see cref="SimulatedTradePosition"/> — đúng lớp mà kiểm
/// thử lịch sử dùng. Nhờ vậy quy ước cùng-một-nến-thì-tính-stop, cách tính phí theo khối lượng và
/// trượt giá chỉ tồn tại ở MỘT chỗ. Một bản cài đặt thứ hai bằng SQL hay bằng vòng lặp tay sẽ trôi
/// khỏi bản gốc, và trôi ở đúng chỗ khiến hai báo cáo cùng nói về một ngày lại ra hai kết luận.
///
/// Kế hoạch thực thi ở đây là bản TỐI GIẢN có chủ ý: một điểm vào thị trường, chốt trọn ở mục tiêu
/// đầu, không scale-in, không trailing. Lý do là phiếu bị veto chưa bao giờ có kế hoạch thực thi —
/// <see cref="TradeExecutionPlanner"/> từ chối phiếu không phải <c>Entered</c>. Dựng kế hoạch đầy
/// đủ cho phiếu bị chặn là bịa ra một thứ chưa từng tồn tại. Đổi lại, mọi phiếu được đo bằng cùng
/// một thước nên số liệu so sánh được với nhau — đó mới là thứ phép đo này cần.
/// </remarks>
public sealed class ScorecardOutcomeReviewService : IScorecardOutcomeReviewService
{
    /// <summary>
    /// Tăng số này MỖI KHI đổi luật phân giải (horizon, khung nến, cách dựng kế hoạch, quy ước
    /// khớp). Bản ghi cũ giữ nguyên phiên bản của chúng và không bị trộn vào thống kê mới.
    /// </summary>
    public const int ResolverVersion = 1;

    private const string BarInterval = "15m";
    private const int BarMinutes = 15;
    private const int HorizonBars = 96;   // 24 giờ

    private readonly MmwDbContext _db;
    private readonly IKlineArchiveReader _archive;
    private readonly IClock _clock;
    private readonly ILogger<ScorecardOutcomeReviewService> _logger;

    public ScorecardOutcomeReviewService(
        MmwDbContext db,
        IKlineArchiveReader archive,
        IClock clock,
        ILogger<ScorecardOutcomeReviewService> logger)
    {
        _db = db;
        _archive = archive;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> ResolvePendingAsync(
        DateTime utcNow, int maxCards = 500, CancellationToken ct = default)
    {
        var settingsByAccount = await _db.EngineSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.TradingAccountId, ct);

        if (settingsByAccount.Count == 0)
        {
            _logger.LogWarning("Không có EngineSetting nào; bỏ qua lượt phân giải kết cục phiếu.");
            return 0;
        }

        // Chỉ lấy phiếu ĐỦ ĐIỀU KIỆN MÔ PHỎNG. Ba điều kiện phía dưới không phải phòng thủ thừa:
        //  - thiếu mức giá  ⟹ không dựng được kế hoạch;
        //  - entry sai phía stop ⟹ setup đã bị phủ định ngay lúc chấm, mô phỏng nó là vô nghĩa;
        //  - mục tiêu sai phía entry ⟹ kế hoạch tự mâu thuẫn.
        // Lọc ngay trong truy vấn để chúng không quay lại mỗi giờ rồi ném ngoại lệ đều đặn.
        var horizonEndsBefore = utcNow.AddMinutes(-BarMinutes);

        var pending = await _db.EntryScorecards
            .AsNoTracking()
            .Where(c => c.Direction != null
                        && c.SuggestedEntry != null
                        && c.SuggestedStopLoss != null
                        && (c.SuggestedFirstTakeProfit ?? c.SuggestedTakeProfit) != null
                        && c.EvaluatedAtUtc <= horizonEndsBefore
                        && !_db.ScorecardOutcomeReviews.Any(
                            r => r.EntryScorecardId == c.Id && r.ResolverVersion == ResolverVersion))
            .OrderBy(c => c.EvaluatedAtUtc)
            .Take(maxCards)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var created = 0;
        var waiting = 0;
        var unsimulatable = 0;

        foreach (var card in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (!settingsByAccount.TryGetValue(card.TradingAccountId, out var setting)) continue;

            var plan = BuildPlan(card);
            if (plan is null) { unsimulatable++; continue; }

            var review = await ResolveAsync(card, plan, setting, ct);
            if (review is null) { waiting++; continue; }

            _db.ScorecardOutcomeReviews.Add(review);
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Phân giải kết cục phiếu: tạo {Created}, chờ thêm nến {Waiting}, không mô phỏng được " +
            "{Unsimulatable}, xét {Total} phiếu (luật v{Version}).",
            created, waiting, unsimulatable, pending.Count, ResolverVersion);

        return created;
    }

    /// <summary>
    /// Kế hoạch tối giản từ chính các mức giá phiếu đã ghi. <c>null</c> nghĩa là phiếu tự mâu
    /// thuẫn về mặt hình học và không đáng mô phỏng.
    /// </summary>
    private static TradeExecutionPlan? BuildPlan(EntryScorecard card)
    {
        if (card.Direction is not { } direction
            || card.SuggestedEntry is not { } entry
            || card.SuggestedStopLoss is not { } stop) return null;

        if ((card.SuggestedFirstTakeProfit ?? card.SuggestedTakeProfit) is not { } takeProfit)
            return null;

        var stopOnCorrectSide = direction == TradeDirection.Long ? entry > stop : entry < stop;
        var targetOnCorrectSide = direction == TradeDirection.Long ? takeProfit > entry : takeProfit < entry;
        if (!stopOnCorrectSide || !targetOnCorrectSide) return null;

        return new TradeExecutionPlan(
            Entries: new[] { new PlannedEntryTranche(entry, RiskWeight: 1m, IsLimit: false) },
            StopLoss: stop,
            FirstTakeProfit: takeProfit,
            RunnerTakeProfit: null,
            FirstTakeProfitFraction: 1m,
            MoveRunnerStopToBreakeven: false,
            Mode: "ShadowReview");
    }

    /// <summary><c>null</c> nghĩa là chưa đủ nến để kết luận — thử lại lượt sau, KHÔNG ghi bản ghi.</summary>
    private async Task<ScorecardOutcomeReview?> ResolveAsync(
        EntryScorecard card,
        TradeExecutionPlan plan,
        EngineSetting setting,
        CancellationToken ct)
    {
        // Lấy DƯ vài nến rồi mới cắt đúng HorizonBars. Thời điểm chấm điểm gần như luôn rơi vào
        // giữa một cây nến, nên nến hợp lệ đầu tiên mở MUỘN hơn nó — hỏi kho đúng
        // `EvaluatedAtUtc + HorizonBars × nến` sẽ luôn thiếu một nến, và phiếu không chạm mức nào
        // sẽ chờ mãi mà không bao giờ đủ điều kiện kết luận.
        var fetchEnd = card.EvaluatedAtUtc.AddMinutes(BarMinutes * (HorizonBars + 2));

        var raw = await _archive.GetRangeAsync(
            card.Symbol, BarInterval, card.EvaluatedAtUtc, fetchEnd, ct);

        // Không nhìn trộm: nến đang chạy dở lúc chấm điểm bị loại, chỉ lấy nến MỞ SAU quyết định.
        // Và chỉ lấy nến đã đóng — nến đang hình thành có High/Low còn đổi theo từng tick.
        var candles = raw
            .Where(c => c.OpenTime >= card.EvaluatedAtUtc)
            .OrderBy(c => c.OpenTime)
            .Take(HorizonBars)
            .ToList()
            .ClosedOnly(_clock);

        if (candles.Count == 0) return null;

        var position = SimulatedTradePosition.Open(
            card.Symbol,
            card.Direction!.Value,
            candles[0].OpenTime,
            sizeR: 1m,               // 1R ⟹ RealizedR đọc thẳng ra R, không phải chuẩn hoá lại
            card.EffectiveDayRegime ?? DayRegime.Range,
            plan,
            setting);

        var bars = 0;
        foreach (var candle in candles)
        {
            bars++;
            if (position.Advance(candle, setting)) break;
        }

        if (!position.IsClosed)
        {
            // Chưa chạm mức nào. Chỉ được kết luận "đi ngang suốt cửa sổ" khi cửa sổ đã ĐỦ nến;
            // thiếu nến mà đóng cưỡng bức là biến "chưa đo được" thành một kết quả giả.
            if (candles.Count < HorizonBars) return null;
            position.CloseAtMarket(candles[^1], setting);
        }

        var entryPrice = plan.Entries[0].Price;
        var netR = position.RealizedR;
        var costR = position.FeeR + position.SlippageR + position.FundingR;

        return new ScorecardOutcomeReview
        {
            EntryScorecardId = card.Id,
            ResolvedAtUtc = _clock.UtcNow,
            ResolverVersion = ResolverVersion,
            BarInterval = BarInterval,
            HorizonBars = HorizonBars,
            FirstBarUtc = candles[0].OpenTime,
            Outcome = MapOutcome(position.ExitReason),
            ExitAtUtc = position.ClosedAtUtc,
            BarsToExit = bars,
            GrossR = netR + costR,
            FeeR = position.FeeR,
            SlippageR = position.SlippageR,
            FundingR = position.FundingR,
            NetR = netR,
            StopDistancePercent = entryPrice > 0m
                ? Math.Abs(entryPrice - plan.StopLoss) / entryPrice * 100m
                : 0m,
            MaxFavorableExcursionR = position.MaxFavorableExcursionR,
            MaxAdverseExcursionR = position.MaxAdverseExcursionR,
        };
    }

    private static ScorecardReviewOutcome MapOutcome(BacktestExitReason? reason) => reason switch
    {
        BacktestExitReason.Target => ScorecardReviewOutcome.Target,
        BacktestExitReason.Stop => ScorecardReviewOutcome.Stop,
        BacktestExitReason.TimeStop => ScorecardReviewOutcome.TimeStop,
        _ => ScorecardReviewOutcome.OpenAtHorizon,
    };
}
