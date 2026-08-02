namespace MMW.Infrastructure.MacroEvents;

public class MacroEventOptions
{
    public const string Section = "MacroEvents";

    public bool Enabled { get; set; } = true;
    public string? CalendarJsonUrl { get; set; }
    public TradingEconomicsCalendarOptions TradingEconomics { get; set; } = new();
    public List<string> NewsRssUrls { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 15;
    public int CacheMinutes { get; set; } = 10;
}

public class TradingEconomicsCalendarOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.tradingeconomics.com";
    public string? ApiKey { get; set; }
    public List<string> Countries { get; set; } = [];
    public int MinImportance { get; set; } = 3;
}
