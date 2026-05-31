using MMW.Domain.Enums;

namespace MMW.Application.MarketData.Models;

/// <summary>Kết quả phân tích deterministic của một symbol từ indicator.</summary>
public sealed record MarketAnalysis(
    decimal Price,
    decimal? Rsi,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Macd,
    decimal? MacdSignal,
    decimal? MacdHistogram,
    decimal? Atr,
    MarketBias Bias,
    int Score,
    string Notes);
