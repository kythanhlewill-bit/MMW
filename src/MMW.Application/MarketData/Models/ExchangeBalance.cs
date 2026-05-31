namespace MMW.Application.MarketData.Models;

/// <summary>Số dư một tài sản trên sàn.</summary>
public sealed record ExchangeBalance(string Asset, decimal Free, decimal Locked)
{
    public decimal Total => Free + Locked;
}
