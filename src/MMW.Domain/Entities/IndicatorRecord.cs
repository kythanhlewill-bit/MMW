using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Lịch sử chỉ số: mỗi lần quét append 1 bản ghi cho mỗi (Symbol, Interval).
/// (MarketSnapshot giữ "mới nhất"; bảng này giữ toàn bộ lịch sử.)
/// </summary>
public class IndicatorRecord : BaseEntity
{
    [Required, MaxLength(30)]
    public string Symbol { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Interval { get; set; } = "1h";

    [Precision(18, 8)] public decimal Price { get; set; }
    [Precision(9, 4)] public decimal? Rsi { get; set; }
    [Precision(18, 8)] public decimal? Ema20 { get; set; }
    [Precision(18, 8)] public decimal? Ema50 { get; set; }
    [Precision(18, 8)] public decimal? Macd { get; set; }
    [Precision(18, 8)] public decimal? MacdSignal { get; set; }
    [Precision(18, 8)] public decimal? MacdHistogram { get; set; }
    [Precision(18, 8)] public decimal? Atr { get; set; }

    public MarketBias Bias { get; set; } = MarketBias.Neutral;

    public DateTime ScannedAt { get; set; }
}
