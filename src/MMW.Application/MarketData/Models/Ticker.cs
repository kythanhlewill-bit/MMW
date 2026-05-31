namespace MMW.Application.MarketData.Models;

/// <summary>Giá hiện tại của một symbol.</summary>
public sealed record Ticker(string Symbol, decimal Price);
