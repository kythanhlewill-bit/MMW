using MMW.Application.Models;
using MMW.Domain.Entities;

namespace MMW.Web.Models;

public class TradeJournalViewModel
{
    public IReadOnlyList<TradeDto> Trades { get; set; } = new List<TradeDto>();
    public Dictionary<long, TradeAnalysis> Analyses { get; set; } = new();
}
