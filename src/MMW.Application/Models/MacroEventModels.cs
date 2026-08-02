// MacroEventImpact đã chuyển sang MMW.Domain.Enums: thực thể ScheduledEvent nằm ở Domain cần
// dùng nó, mà Domain không tham chiếu ngược lên Application được.
using MMW.Domain.Enums;

namespace MMW.Application.Models;

public enum MacroEventKind
{
    EconomicCalendar = 1,
    CentralBank = 2,
    MarketNews = 3,
    Geopolitical = 4,
    Regulation = 5,
}

public class MacroEventModel
{
    public string Source { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public MacroEventKind Kind { get; set; } = MacroEventKind.MarketNews;
    public MacroEventImpact Impact { get; set; } = MacroEventImpact.Medium;
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Currency { get; set; }
    public string? Url { get; set; }
    public DateTime? OccursAtUtc { get; set; }
}

public class MacroEventContext
{
    public bool IsConfigured { get; set; }
    public bool HasBlockingEvent { get; set; }
    public string Summary { get; set; } = "";
    public IReadOnlyList<MacroEventModel> Events { get; set; } = [];
    public IReadOnlyList<MacroEventModel> BlockingEvents { get; set; } = [];
    public List<string> RiskWarnings { get; set; } = [];
}
