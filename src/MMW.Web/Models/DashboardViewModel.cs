using MMW.Domain.Entities;

namespace MMW.Web.Models;

public class DashboardViewModel
{
    public long? SelectedAccountId { get; set; }
    public string? AccountName { get; set; }
    public string Currency { get; set; } = "USDT";
    public decimal Balance { get; set; }
    public decimal? LiveBalance { get; set; }
    public string? LiveBalanceFetchError { get; set; }

    public int TotalTrades { get; set; }
    public int OpenTrades { get; set; }
    public int ClosedTrades { get; set; }
    public int WinTrades { get; set; }
    public int LossTrades { get; set; }
    public decimal TotalPnl { get; set; }
    public int TotalFlags { get; set; }
    public int CriticalFlags { get; set; }

    public IReadOnlyList<TradingAccount> Accounts { get; set; } = new List<TradingAccount>();
}
