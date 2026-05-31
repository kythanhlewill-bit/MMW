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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ITradeWorkflowService _workflow;

    public TradeService(
        IBaseRepository<Trade> tradeRepository,
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<RiskSetting> riskSettings,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ITradeWorkflowService workflow)
    {
        _tradeRepository = tradeRepository;
        _signals = signals;
        _accounts = accounts;
        _riskSettings = riskSettings;
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
            OpenedAt = DateTime.UtcNow,
            Note = $"Tạo từ đề xuất #{signal.Id} ({signal.Symbol} {signal.Direction})",
        };

        return await CreateAsync(dto);
    }
}
