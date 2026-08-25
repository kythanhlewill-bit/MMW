using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Constants;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Biến một phiếu chấm điểm kết luận VÀO LỆNH thành lệnh thật trên sàn.
/// </summary>
/// <remarks>
/// Đây là mắt xích còn thiếu giữa engine tất định và sàn: <c>SignalEvalService</c> cố ý dừng ở
/// việc ghi phiếu (nó không được phép biết gì về sàn để giữ đúng SC-001 — vòng quyết định chạy
/// trọn vẹn khi AI chết), còn <c>LiveOrderService</c> chỉ nhận vào một <c>Trade</c> đã tồn tại.
/// Không có lớp này thì phiếu <c>Entered</c> nằm im trong bảng và tuần chạy thử ra 0 lệnh.
///
/// Đặt ở tầng điều phối chứ không nhét vào <c>SignalEvalService</c> là có chủ ý: gộp vào đó sẽ
/// kéo phụ thuộc sàn vào đúng lớp mà cả spec lẫn kiểm thử lịch sử dựa vào tính thuần tuý của nó.
///
/// Lớp này KHÔNG tự phán xét rủi ro. Mọi rào — cap đòn bẩy, cap notional, trần lệnh/ngày, rule
/// Critical, chống trùng vị thế, và cả công tắc tổng <c>LiveTrading.Enabled</c> — đều nằm trong
/// <c>LiveOrderService</c> và vẫn chạy y nguyên. Ở đây chỉ có phép dịch phiếu → lệnh.
/// </remarks>
public interface IScorecardExecutionService
{
    /// <summary>
    /// Tạo lệnh và gửi sàn cho những phiếu kết luận vào lệnh. Trả về số lệnh đã tạo.
    /// </summary>
    Task<int> ExecuteAsync(IReadOnlyList<EntryScorecard> scorecards, CancellationToken ct = default);
}

public sealed class ScorecardExecutionService : IScorecardExecutionService
{
    private readonly ITradeService _trades;
    private readonly ILiveOrderService _liveOrders;
    private readonly IBaseRepository<Trade> _tradeRepository;
    private readonly IBaseRepository<EntryScorecard> _scorecards;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IBaseRepository<EngineSetting> _engineSettings;
    private readonly ITradeExecutionPlanner _executionPlanner;
    private readonly ILiveBalanceService _liveBalance;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ScorecardExecutionService> _logger;

    public ScorecardExecutionService(
        ITradeService trades,
        ILiveOrderService liveOrders,
        IBaseRepository<Trade> tradeRepository,
        IBaseRepository<EntryScorecard> scorecards,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IBaseRepository<EngineSetting> engineSettings,
        ITradeExecutionPlanner executionPlanner,
        ILiveBalanceService liveBalance,
        IUnitOfWork unitOfWork,
        ILogger<ScorecardExecutionService> logger)
    {
        _trades = trades;
        _liveOrders = liveOrders;
        _tradeRepository = tradeRepository;
        _scorecards = scorecards;
        _accounts = accounts;
        _riskSettings = riskSettings;
        _engineSettings = engineSettings;
        _executionPlanner = executionPlanner;
        _liveBalance = liveBalance;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(IReadOnlyList<EntryScorecard> scorecards, CancellationToken ct = default)
    {
        var created = 0;

        foreach (var card in scorecards)
        {
            try
            {
                if (await TryExecuteOneAsync(card, ct)) created++;
            }
            catch (Exception ex)
            {
                // Một phiếu lỗi không được chặn các phiếu còn lại của cùng chu kỳ.
                _logger.LogError(ex, "Lỗi tạo lệnh từ phiếu #{ScorecardId} ({Symbol}).", card.Id, card.Symbol);
            }
        }

        return created;
    }

    private async Task<bool> TryExecuteOneAsync(EntryScorecard card, CancellationToken ct)
    {
        if (card.Outcome != ScorecardOutcome.Entered) return false;
        if (card.IsBacktest) return false;

        // Idempotency. Phiếu đã sinh lệnh thì thôi — job chạy lại (Hangfire retry, redeploy giữa
        // chu kỳ) không được đẻ lệnh thứ hai cho cùng một cây nến.
        if (card.TradeId is not null)
        {
            _logger.LogDebug("Phiếu #{ScorecardId} đã gắn lệnh #{TradeId} — bỏ qua.", card.Id, card.TradeId);
            return false;
        }

        if (card.Direction is not TradeDirection direction)
        {
            _logger.LogWarning("Phiếu #{ScorecardId} kết luận vào lệnh nhưng không có hướng — bỏ qua.", card.Id);
            return false;
        }

        // Lấy kế hoạch từ ĐÚNG hàm mà cổng chi phí đã dùng để chấm phiếu này. Đọc thẳng các mức
        // giá trên phiếu như trước là chỗ hai bên lệch nhau: cổng chấm một kế hoạch còn sàn nhận
        // một lệnh khác. Đi qua planner thì không còn hai nguồn sự thật để mà lệch.
        var engineSetting = await _engineSettings.FirstOrDefaultAsync(e => e.TradingAccountId == card.TradingAccountId);

        var plan = _executionPlanner.PlanLive(card, engineSetting);
        if (plan is null)
        {
            _logger.LogWarning(
                "Phiếu #{ScorecardId} {Symbol} kết luận vào lệnh nhưng thiếu mức giá (entry={Entry}, sl={Sl}, tp={Tp}) — bỏ qua.",
                card.Id, card.Symbol, card.SuggestedEntry, card.SuggestedStopLoss, card.SuggestedTakeProfit);
            return false;
        }

        if (card.FinalSizeR <= 0m)
        {
            _logger.LogWarning("Phiếu #{ScorecardId} {Symbol} có FinalSizeR = 0 — không có gì để vào.", card.Id, card.Symbol);
            return false;
        }

        var tranche = plan.Entries[0];
        var entry = tranche.Price;
        var stop = plan.StopLoss;
        var target = plan.FirstTakeProfit;
        var stopDistance = Math.Abs(entry - stop);

        var account = await _accounts.FindAsync(card.TradingAccountId);
        if (account is null || !account.IsActive)
        {
            _logger.LogWarning("Phiếu #{ScorecardId}: tài khoản #{AccountId} không tồn tại hoặc đã tắt.", card.Id, card.TradingAccountId);
            return false;
        }

        // Chặn trùng TRƯỚC khi tạo. LiveOrderService cũng chống trùng, nhưng nó chống bằng cách
        // tạo lệnh rồi huỷ — mỗi 15 phút một bản ghi Cancelled sẽ chôn vùi nhật ký của tuần thử.
        var hasOpenSame = await _tradeRepository.AnyAsync(t =>
            t.TradingAccountId == account.Id &&
            t.Symbol == card.Symbol &&
            t.Direction == direction &&
            t.Status == TradeStatus.Open);
        if (hasOpenSame)
        {
            _logger.LogInformation(
                "Phiếu #{ScorecardId}: đã có lệnh {Direction} {Symbol} đang mở — bỏ qua.", card.Id, direction, card.Symbol);
            return false;
        }

        var riskSetting = await _riskSettings.FirstOrDefaultAsync(r => r.TradingAccountId == account.Id)
                          ?? new RiskSetting { TradingAccountId = account.Id };

        // Vốn để tính cỡ lệnh phải là vốn mà LỆNH NÀY tiêu được, nghĩa là số dư của đúng ví trả
        // ký quỹ cho cặp đó. Ở chế độ ký quỹ đơn tài sản, ví USDT và ví USDC không dùng chung
        // được một đồng nào; một lệnh BTCUSDC tính cỡ theo ví USDT là tính theo túi tiền của
        // người khác.
        //
        // Trước đây chỗ này đọc thẳng account.CurrentBalance — sổ nội bộ, chỉ nhúc nhích khi
        // chính app đóng lệnh. Nạp/rút trên sàn hoặc một fill mà app đọc hụt đều làm nó trôi
        // khỏi thực tế trong im lặng, và mọi lệnh sau đó vào sai cỡ mà không có cảnh báo nào.
        // ILiveBalanceService tự rơi về CurrentBalance khi tài khoản chưa có key hoặc gọi sàn
        // lỗi, nên đường cũ vẫn còn nguyên làm lưới đỡ.
        var quoteAsset = SymbolConventions.QuoteAssetOf(card.Symbol);
        var balance = await _liveBalance.GetEffectiveBalanceAsync(account, quoteAsset, ct);
        if (balance <= 0m)
        {
            _logger.LogWarning(
                "Phiếu #{ScorecardId} {Symbol}: ví {Asset} không có số dư dùng được — bỏ qua.",
                card.Id, card.Symbol, quoteAsset);
            return false;
        }

        // FinalSizeR là kích thước tính theo R, với 1R = MaxRiskPerTradePercent phần trăm vốn.
        // Quy đổi sang khối lượng chính là phép chia cho khoảng cách dừng lỗ — cùng công thức
        // mà CreateFromSignalAsync đang dùng cho đường tín hiệu AI, chỉ nhân thêm hệ số R.
        // Ngưỡng rủi ro đọc theo NHÓM của chính phiếu này. Nhóm swing có cột riêng vì nó có thể
        // chưa từng chạy thật lần nào, trong khi nhóm ngắn đã có lịch sử để biện minh cho cỡ lệnh
        // của mình — dùng chung nghĩa là lệnh swing đầu tiên vào bằng cỡ đó.
        var maxRiskPercent = riskSetting.MaxRiskPerTradePercentOf(card.Style);
        var riskAmount = balance * maxRiskPercent / 100m * card.FinalSizeR;
        var quantity = Math.Round(riskAmount / stopDistance, 8, MidpointRounding.AwayFromZero);
        if (quantity <= 0m)
        {
            _logger.LogWarning(
                "Phiếu #{ScorecardId} {Symbol}: khối lượng tính ra 0 (ví {Asset} {Balance}, risk {Risk}%, {SizeR}R) — bỏ qua.",
                card.Id, card.Symbol, quoteAsset, balance, maxRiskPercent, card.FinalSizeR);
            return false;
        }

        var tradeId = await _trades.CreateAsync(new TradeDto
        {
            TradingAccountId = account.Id,
            Symbol = card.Symbol,
            Direction = direction,
            Status = TradeStatus.Open,
            Source = TradeSource.Api,
            // Kiểu lệnh theo kế hoạch, không ghi cứng: đây là chỗ DUY NHẤT cần đổi khi chuyển
            // sang vào bằng lệnh chờ, và cổng chi phí sẽ tự tính phí maker theo cùng cờ đó.
            OrderType = tranche.IsLimit ? OrderType.Limit : OrderType.Market,
            EntryPrice = entry,
            StopLoss = stop,
            // TakeProfit là mục tiêu CUỐI. Khi kế hoạch có hai mục tiêu thì `plan.FirstTakeProfit`
            // là mục tiêu gần, còn mục tiêu cuối nằm ở `RunnerTakeProfit` — nên chỗ này không
            // được đọc thẳng `FirstTakeProfit` như trước, nếu không toàn bộ phần runner biến mất
            // đúng lúc nó vừa được dựng ra.
            TakeProfit = plan.RunnerTakeProfit ?? target,
            FirstTakeProfit = plan.RunnerTakeProfit is null ? null : target,
            FirstTakeProfitFraction = plan.RunnerTakeProfit is null ? null : plan.FirstTakeProfitFraction,
            InitialStopLoss = stop,
            TrailPivotBars = plan.TrailRunnerPivotBars,
            Style = card.Style,
            Quantity = quantity,
            // Để trống đòn bẩy: LiveOrderService rơi về LiveTrading.DefaultLeverage, nghĩa là đổi
            // cấu hình đòn bẩy chỉ phải sửa một chỗ.
            Leverage = null,
            PlannedRiskReward = card.RiskReward,
            OpenedAt = DateTime.UtcNow,
            Note = $"Engine V{(int)card.StrategyVersion} · phiếu #{card.Id} · {card.SetupType} · "
                 + $"điểm {card.TotalScore}/{card.AvailableMaxPoints} · size {card.FinalSizeR:N2}R",
        });

        // Gắn phiếu ↔ lệnh TRƯỚC khi gửi sàn. Nếu gửi sàn chết giữa chừng, lần chạy sau vẫn thấy
        // phiếu đã có lệnh và không tạo lệnh trùng; lệnh dở dang thuộc về job retry, không thuộc
        // về vòng chấm điểm.
        var tracked = await _scorecards.FindAsync(card.Id);
        if (tracked is not null)
        {
            tracked.TradeId = tradeId;
            _scorecards.Update(tracked);
            await _unitOfWork.CommitAsync(ct);
        }
        card.TradeId = tradeId;

        _logger.LogInformation(
            "Phiếu #{ScorecardId} {Symbol} {Direction} ⟹ lệnh #{TradeId} (qty {Qty}, entry {Entry}, SL {Sl}, TP {Tp}) — gửi sàn.",
            card.Id, card.Symbol, direction, tradeId, quantity, entry, stop, target);

        await _liveOrders.PlaceForTradeAsync(tradeId, ct);
        return true;
    }
}
