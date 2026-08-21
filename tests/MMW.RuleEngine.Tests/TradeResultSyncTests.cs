using MMW.Application.MarketData.Models;
using MMW.Application.Services;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Ghép fill của sàn với lệnh trong nhật ký.
/// </summary>
/// <remarks>
/// Toàn bộ nhóm này sinh ra từ một sự cố thật trên testnet: năm lệnh liên tiếp được ghi là đã
/// đóng đúng MỘT GIÂY sau khi mở, giá ra bằng giá vào, lãi lỗ bằng đúng tiền phí. Nguyên nhân là
/// <c>ExchangeOrderId</c> — id lệnh VÀO — được dùng để nhận diện fill ĐÓNG lệnh. Ngay giây lệnh
/// vào khớp, chính cú khớp đó bị đọc thành cú đóng.
///
/// Cái giá của lỗi này không nằm ở con số lãi lỗ sai. Nó nằm ở chỗ engine tin rằng mình đang
/// rảnh tay trong khi vị thế thật vẫn chạy trên sàn — nên nó mở tiếp lệnh mới, và mọi rào hạn
/// mức vị thế đều đọc từ nhật ký đã sai.
/// </remarks>
public sealed class TradeResultSyncTests
{
    private static readonly DateTime Open = new(2026, 8, 20, 16, 16, 10, DateTimeKind.Utc);

    private static Trade LongTrade(string? orderId = "111") => new()
    {
        Id = 1,
        Symbol = "BTCUSDT",
        Direction = TradeDirection.Long,
        Status = TradeStatus.Open,
        EntryPrice = 72354.20m,
        StopLoss = 71397.80m,
        TakeProfit = 74000m,
        Quantity = 0.01m,
        Fee = 0.5m,
        OpenedAt = Open,
        CreatedDate = Open,
        ExchangeOrderId = orderId,
    };

    private static ExchangeTrade Fill(
        bool isBuyer, decimal price, decimal qty, DateTime time, string orderId) =>
        new("f", "BTCUSDT", isBuyer, price, qty, 0.3m, "USDT", time, orderId);

    private static TradingAccount Account() => new() { Id = 1, CurrentBalance = 1000m };

    /// <summary>Chính cú khớp lệnh vào KHÔNG được đọc thành cú đóng lệnh.</summary>
    [Fact]
    public void Fill_cua_lenh_vao_khong_dong_lenh()
    {
        var trade = LongTrade();
        var fills = new[] { Fill(isBuyer: true, 72354.20m, 0.01m, Open.AddSeconds(1), "111") };

        var closed = TradeResultSyncService.TryMatchAndClose(trade, fills, Account(), null);

        Assert.False(closed);
        Assert.Equal(TradeStatus.Open, trade.Status);
        Assert.Null(trade.ExitPrice);
    }

    /// <summary>Fill của lệnh dừng lỗ — id khác, phía ngược lại — thì đóng.</summary>
    [Fact]
    public void Fill_cua_dung_lo_thi_dong_lenh()
    {
        var trade = LongTrade();
        var fills = new[]
        {
            Fill(isBuyer: true, 72354.20m, 0.01m, Open.AddSeconds(1), "111"),
            Fill(isBuyer: false, 71397.80m, 0.01m, Open.AddMinutes(30), "222"),
        };

        var closed = TradeResultSyncService.TryMatchAndClose(trade, fills, Account(), null);

        Assert.True(closed);
        Assert.Equal(TradeStatus.Closed, trade.Status);
        Assert.Equal(71397.80m, trade.ExitPrice);
        Assert.Equal(Open.AddMinutes(30), trade.ClosedAt);
        Assert.Equal(TradeOutcome.Loss, trade.Outcome);
    }

    /// <summary>
    /// Có id lệnh vào mà sàn chưa có fill nào của nó thì lệnh chờ chưa khớp — không đóng.
    /// </summary>
    /// <remarks>
    /// Không có rào này, một lệnh chờ đang treo sẽ nuốt fill của lệnh TRƯỚC ĐÓ trên cùng mã:
    /// đúng phía, đúng dải giá, chỉ sai ở chỗ nó thuộc về một vị thế đã đóng từ lâu.
    /// </remarks>
    [Fact]
    public void Lenh_cho_chua_khop_thi_khong_dong()
    {
        var trade = LongTrade();
        var fills = new[] { Fill(isBuyer: false, 72000m, 0.01m, Open.AddMinutes(5), "999") };

        var closed = TradeResultSyncService.TryMatchAndClose(trade, fills, Account(), null);

        Assert.False(closed);
        Assert.Equal(TradeStatus.Open, trade.Status);
    }

    /// <summary>Vị thế vẫn còn trên sàn thì không đóng, dù fill trông có vẻ khớp.</summary>
    [Fact]
    public void Vi_the_con_mo_tren_san_thi_khong_dong()
    {
        var trade = LongTrade();
        var fills = new[]
        {
            Fill(isBuyer: true, 72354.20m, 0.01m, Open.AddSeconds(1), "111"),
            Fill(isBuyer: false, 71397.80m, 0.01m, Open.AddMinutes(30), "222"),
        };

        var closed = TradeResultSyncService.TryMatchAndClose(
            trade, fills, Account(), ["BTCUSDT|Long"]);

        Assert.False(closed);
        Assert.Equal(TradeStatus.Open, trade.Status);
    }

    /// <summary>Lệnh nhập tay không có id vẫn đóng được bằng phía + thời điểm + dải giá.</summary>
    [Fact]
    public void Lenh_nhap_tay_khong_co_id_van_dong_duoc()
    {
        var trade = LongTrade(orderId: null);
        var fills = new[] { Fill(isBuyer: false, 74000m, 0.01m, Open.AddMinutes(30), "333") };

        var closed = TradeResultSyncService.TryMatchAndClose(trade, fills, Account(), null);

        Assert.True(closed);
        Assert.Equal(74000m, trade.ExitPrice);
        Assert.Equal(TradeOutcome.Win, trade.Outcome);
    }

    /// <summary>Khớp chưa đủ 90% khối lượng thì lệnh vẫn mở — đóng một phần không phải đóng.</summary>
    [Fact]
    public void Dong_mot_phan_thi_chua_dong()
    {
        var trade = LongTrade();
        var fills = new[]
        {
            Fill(isBuyer: true, 72354.20m, 0.01m, Open.AddSeconds(1), "111"),
            Fill(isBuyer: false, 71397.80m, 0.004m, Open.AddMinutes(30), "222"),
        };

        var closed = TradeResultSyncService.TryMatchAndClose(trade, fills, Account(), null);

        Assert.False(closed);
        Assert.Equal(TradeStatus.Open, trade.Status);
    }
}
