using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Domain.Entities;

namespace MMW.Web.Models;

public class TradeJournalViewModel
{
    public IReadOnlyList<TradeDto> Trades { get; set; } = new List<TradeDto>();
    public Dictionary<long, TradeAnalysis> Analyses { get; set; } = new();

    /// <summary>Lệnh chờ đọc trực tiếp từ sàn (read-only, không lưu DB).</summary>
    public IReadOnlyList<OpenOrderRow> OpenOrders { get; set; } = new List<OpenOrderRow>();

    /// <summary>Có lấy được lệnh chờ từ sàn không (false = lỗi đọc, để view báo nhẹ).</summary>
    public bool OpenOrdersLoaded { get; set; } = true;
}

/// <summary>1 dòng lệnh chờ trên sàn kèm tài khoản (id để huỷ, name để hiển thị).</summary>
public record OpenOrderRow(long AccountId, string AccountName, ExchangeOpenOrder Order);
