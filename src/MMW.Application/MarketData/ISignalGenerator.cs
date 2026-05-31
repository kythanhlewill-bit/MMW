using MMW.Application.MarketData.Models;
using MMW.Domain.Enums;

namespace MMW.Application.MarketData;

/// <summary>Đề xuất lệnh sinh tự động (chưa gắn symbol/thời gian — service bổ sung).</summary>
public sealed record SuggestedSignal(
    TradeDirection Direction,
    MarketBias Bias,
    int Score,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    decimal RiskReward,
    string Reason);

/// <summary>
/// Sinh đề xuất lệnh deterministic từ phân tích: chỉ khi tín hiệu đủ mạnh (trend + MACD đồng thuận)
/// và có ATR. Entry = giá hiện tại; SL/TP đặt theo bội số ATR.
/// </summary>
public interface ISignalGenerator
{
    /// <param name="minScore">Điểm tối thiểu để coi là tín hiệu đủ mạnh (cấu hình động).</param>
    SuggestedSignal? Generate(MarketAnalysis analysis, int minScore = 2);
}
