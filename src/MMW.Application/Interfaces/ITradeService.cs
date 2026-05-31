using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface ITradeService
{
    Task<IReadOnlyList<TradeDto>> GetAllAsync();
    Task<TradeDto?> GetByIdAsync(long id);
    Task<long> CreateAsync(TradeDto dto);

    /// <summary>
    /// Tạo lệnh journal từ một đề xuất (TradeSignal). Tự tính khối lượng theo % rủi ro của tài khoản
    /// (quantity = vốn × MaxRiskPerTradePercent / |Entry − StopLoss|), rồi chạy Rule Engine + Behavior.
    /// </summary>
    Task<long> CreateFromSignalAsync(long signalId, long accountId, CancellationToken cancellationToken = default);
}
