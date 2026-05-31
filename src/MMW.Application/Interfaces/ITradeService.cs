using MMW.Application.Models;
using MMW.Domain.Enums;

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

    /// <summary>
    /// Đóng một lệnh đang mở theo giá thoát: tính RealizedPnl, cộng/trừ vào số dư tài khoản,
    /// rồi chạy lại workflow (cập nhật Outcome/RMultiple + chấm rule + behavior + tổng hợp ngày).
    /// </summary>
    Task CloseAsync(long tradeId, decimal exitPrice, EmotionState emotionAfter, CancellationToken cancellationToken = default);

    /// <summary>Sửa các trường của một lệnh CHƯA đóng, rồi chấm lại rule + behavior.</summary>
    Task UpdateAsync(long tradeId, TradeDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá một lệnh: nếu đã đóng thì hoàn lại PnL vào số dư; xoá Flag liên quan;
    /// xoá lệnh (cascade TradeTag/TradeAnalysis); tính lại tổng hợp ngày.
    /// </summary>
    Task DeleteAsync(long tradeId, CancellationToken cancellationToken = default);
}
