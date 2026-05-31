using MMW.Domain.Entities;

namespace MMW.Web.Models;

public class IndicatorHistoryViewModel
{
    public string? Symbol { get; set; }
    public IReadOnlyList<IndicatorRecord> Records { get; set; } = new List<IndicatorRecord>();
}
