namespace MMW.Application.MarketData.Models;

/// <summary>
/// Bước giá (tickSize) của 1 symbol futures + số chữ số thập phân tương ứng.
/// Dùng để làm tròn giá nhập (Entry/SL/TP) đúng quy tắc sàn, tránh lỗi -1111.
/// </summary>
public record SymbolPriceFilter(decimal TickSize, int PriceDecimals);
