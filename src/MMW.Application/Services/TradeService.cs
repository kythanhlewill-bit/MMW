using AutoMapper;
using Microsoft.EntityFrameworkCore;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Luồng Controller → Service → Repository → UnitOfWork.
/// Khi tạo lệnh xong sẽ tự chạy phân tích (Rule Engine + Behavior + cập nhật TradingDay).
/// </summary>
public class TradeService : ITradeService
{
    private readonly IBaseRepository<Trade> _tradeRepository;
    private readonly IBaseRepository<TradeSignal> _signals;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IBaseRepository<Flag> _flags;
    private readonly ITradingDayService _tradingDayService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ITradeWorkflowService _workflow;

    public TradeService(
        IBaseRepository<Trade> tradeRepository,
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IBaseRepository<Flag> flags,
        ITradingDayService tradingDayService,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ITradeWorkflowService workflow)
    {
        _tradeRepository = tradeRepository;
        _signals = signals;
        _accounts = accounts;
        _riskSettings = riskSettings;
        _flags = flags;
        _tradingDayService = tradingDayService;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _workflow = workflow;
    }

    public async Task<IReadOnlyList<TradeDto>> GetAllAsync()
    {
        var entities = await _tradeRepository.Queryable
            .AsNoTracking()
            .Include(t => t.TradingAccount)
            .OrderByDescending(t => t.OpenedAt ?? t.CreatedDate)
            .ToListAsync();
        return _mapper.Map<List<TradeDto>>(entities);
    }

    public async Task<TradeDto?> GetByIdAsync(long id)
    {
        var entity = await _tradeRepository.FindAsync(id);
        return entity == null ? null : _mapper.Map<TradeDto>(entity);
    }

    public async Task<long> CreateAsync(TradeDto dto)
    {
        var entity = _mapper.Map<Trade>(dto);
        await _tradeRepository.AddAsync(entity);
        await _unitOfWork.CommitAsync();

        // Chấm rủi ro + phát hiện hành vi + cập nhật tổng hợp ngày.
        await _workflow.ProcessTradeAsync(entity.Id);

        return entity.Id;
    }

    public async Task<long> CreateFromSignalAsync(long signalId, long accountId, CancellationToken cancellationToken = default)
    {
        var signal = await _signals.FindAsync(signalId)
            ?? throw new InvalidOperationException($"Không tìm thấy đề xuất #{signalId}.");

        var account = await _accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Không tìm thấy tài khoản #{accountId}.");

        var settings = await _riskSettings.FirstOrDefaultAsync(s => s.TradingAccountId == accountId)
            ?? new RiskSetting();

        var hasOpenSameDirection = await _tradeRepository.AnyAsync(t =>
            t.TradingAccountId == accountId &&
            t.Symbol == signal.Symbol &&
            t.Direction == signal.Direction &&
            t.Status == TradeStatus.Open);

        if (hasOpenSameDirection)
            throw new InvalidOperationException($"Đang có lệnh mở {signal.Direction} {signal.Symbol} trên tài khoản #{accountId}.");

        // Auto-size theo % rủi ro: quantity = (vốn × maxRisk%) / khoảng cách dừng lỗ.
        var stopDistance = Math.Abs(signal.Entry - signal.StopLoss);
        var quantity = 0m;
        if (stopDistance > 0m && account.CurrentBalance > 0m)
        {
            var riskAmount = account.CurrentBalance * settings.MaxRiskPerTradePercent / 100m;
            quantity = Math.Round(riskAmount / stopDistance, 8, MidpointRounding.AwayFromZero);
        }

        var dto = new TradeDto
        {
            TradingAccountId = accountId,
            Symbol = signal.Symbol,
            Direction = signal.Direction,
            Status = TradeStatus.Open,
            Source = TradeSource.Manual,
            EntryPrice = signal.Entry,
            StopLoss = signal.StopLoss,
            TakeProfit = signal.TakeProfit,
            Quantity = quantity,
            Leverage = 20m,
            OpenedAt = DateTime.UtcNow,
            Note = $"Tạo từ đề xuất #{signal.Id} ({signal.Symbol} {signal.Direction})",
        };

        return await CreateAsync(dto);
    }

    public async Task CloseAsync(long tradeId, decimal exitPrice, EmotionState emotionAfter, CancellationToken cancellationToken = default)
    {
        var trade = await _tradeRepository.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy lệnh #{tradeId}.");

        if (trade.Status == TradeStatus.Closed)
            throw new InvalidOperationException("Lệnh đã đóng.");

        // PnL = (chênh lệch giá theo hướng) × khối lượng − phí.
        var gross = trade.Direction == TradeDirection.Long
            ? (exitPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - exitPrice) * trade.Quantity;
        var realizedPnl = Math.Round(gross - trade.Fee, 8, MidpointRounding.AwayFromZero);

        trade.ExitPrice = exitPrice;
        trade.ClosedAt = DateTime.UtcNow;
        trade.Status = TradeStatus.Closed;
        trade.RealizedPnl = realizedPnl;
        trade.EmotionAfter = emotionAfter;

        // Cập nhật số dư tài khoản theo kết quả.
        var account = await _accounts.FindAsync(trade.TradingAccountId);
        if (account is not null)
            account.CurrentBalance += realizedPnl;

        await _unitOfWork.CommitAsync(cancellationToken);

        // Tính lại Outcome/RMultiple + chấm rule + behavior (chuỗi thua) + tổng hợp ngày.
        await _workflow.ProcessTradeAsync(tradeId, cancellationToken);
    }

    public async Task UpdateAsync(long tradeId, TradeDto dto, CancellationToken cancellationToken = default)
    {
        var trade = await _tradeRepository.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy lệnh #{tradeId}.");

        if (trade.Status == TradeStatus.Closed)
            throw new InvalidOperationException("Không sửa được lệnh đã đóng. Hãy xoá nếu cần.");

        trade.StrategyId = dto.StrategyId;
        trade.Symbol = dto.Symbol;
        trade.Direction = dto.Direction;
        trade.OrderType = dto.OrderType;
        trade.Status = dto.Status;
        trade.EntryPrice = dto.EntryPrice;
        trade.StopLoss = dto.StopLoss;
        trade.TakeProfit = dto.TakeProfit;
        trade.Quantity = dto.Quantity;
        trade.Leverage = dto.Leverage;
        trade.Fee = dto.Fee;
        trade.EmotionBefore = dto.EmotionBefore;
        trade.Note = dto.Note;

        await _unitOfWork.CommitAsync(cancellationToken);

        // Chấm lại rule + behavior + tổng hợp ngày theo giá trị mới.
        await _workflow.ProcessTradeAsync(tradeId, cancellationToken);
    }

    public async Task ReactivateAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await _tradeRepository.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy lệnh #{tradeId}.");

        if (trade.Status == TradeStatus.Closed)
            throw new InvalidOperationException("Không kích hoạt lại lệnh đã đóng.");

        // Mở lại nếu bị Cancelled.
        if (trade.Status == TradeStatus.Cancelled)
            trade.Status = TradeStatus.Open;

        // Reset trạng thái live để cho phép thử lại.
        trade.IsLive = false;
        trade.LiveStatus = LiveOrderStatus.None;
        trade.ExchangeOrderId = null;
        trade.ExchangeClientOrderId = null;
        trade.LiveNote = null;

        await _unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await _tradeRepository.FindAsync(tradeId)
            ?? throw new InvalidOperationException($"Không tìm thấy lệnh #{tradeId}.");

        var accountId = trade.TradingAccountId;
        var date = DateOnly.FromDateTime(trade.OpenedAt ?? trade.CreatedDate);

        // Hoàn lại PnL vào số dư nếu lệnh đã đóng.
        if (trade.Status == TradeStatus.Closed && trade.RealizedPnl.HasValue)
        {
            var account = await _accounts.FindAsync(accountId);
            if (account is not null)
                account.CurrentBalance -= trade.RealizedPnl.Value;
        }

        // Flag là NoAction nên phải xoá tay; TradeTag/TradeAnalysis cascade theo Trade.
        var flags = await _flags.FindListAsync(f => f.TradeId == tradeId);
        if (flags.Count > 0)
            _flags.RemoveRange(flags);

        _tradeRepository.Remove(trade);
        await _unitOfWork.CommitAsync(cancellationToken);

        // Cập nhật lại tổng hợp ngày sau khi xoá.
        await _tradingDayService.RecomputeAndSaveAsync(accountId, date, cancellationToken);
    }
}
