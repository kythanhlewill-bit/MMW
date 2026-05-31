namespace MMW.Application.MarketData.Models;

/// <summary>Một lần khớp lệnh (fill) lấy từ lịch sử giao dịch của sàn.</summary>
public sealed record ExchangeTrade(
    string Id,
    string Symbol,
    bool IsBuyer,
    decimal Price,
    decimal Quantity,
    decimal Commission,
    string CommissionAsset,
    DateTime Time);
