using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Enums;

namespace MMW.Infrastructure.MacroEvents;

public class ConfiguredMacroEventProvider : IMacroEventProvider
{
    private const string CacheKey = "macro-events:configured-provider";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly MacroEventOptions _options;
    private readonly ILogger<ConfiguredMacroEventProvider> _logger;

    public ConfiguredMacroEventProvider(
        HttpClient http,
        IMemoryCache cache,
        IOptions<MacroEventOptions> options,
        ILogger<ConfiguredMacroEventProvider> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        _options.Enabled &&
        (!string.IsNullOrWhiteSpace(_options.CalendarJsonUrl)
            || HasTradingEconomicsCalendar()
            || _options.NewsRssUrls.Any(x => !string.IsNullOrWhiteSpace(x)));

    public async Task<IReadOnlyList<MacroEventModel>> GetEventsAsync(
        DateTime utcNow,
        TimeSpan lookAhead,
        TimeSpan newsLookBack,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return [];

        var cacheMinutes = Math.Clamp(_options.CacheMinutes, 1, 60);
        var cacheKey = $"{CacheKey}:{utcNow.Date:yyyyMMdd}:{utcNow.Add(lookAhead).Date:yyyyMMdd}:{lookAhead.TotalHours:0}";
        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheMinutes);
            return await LoadEventsAsync(utcNow, lookAhead, cancellationToken);
        });

        var maxFuture = utcNow.Add(lookAhead);
        var minPast = utcNow.Subtract(newsLookBack);

        return (cached ?? [])
            .Where(e => e.OccursAtUtc is null || (e.OccursAtUtc >= minPast && e.OccursAtUtc <= maxFuture))
            .OrderBy(e => e.OccursAtUtc ?? DateTime.MaxValue)
            .ToList();
    }

    private async Task<IReadOnlyList<MacroEventModel>> LoadEventsAsync(
        DateTime utcNow,
        TimeSpan lookAhead,
        CancellationToken cancellationToken)
    {
        var events = new List<MacroEventModel>();

        if (!string.IsNullOrWhiteSpace(_options.CalendarJsonUrl))
            events.AddRange(await LoadCalendarAsync(_options.CalendarJsonUrl, cancellationToken));

        if (HasTradingEconomicsCalendar())
            events.AddRange(await LoadCalendarAsync(BuildTradingEconomicsCalendarUrl(utcNow, lookAhead), cancellationToken));

        foreach (var url in _options.NewsRssUrls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            events.AddRange(await LoadRssAsync(url, cancellationToken));

        return events
            .Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .GroupBy(e => e.SourceKey)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<IReadOnlyList<MacroEventModel>> LoadCalendarAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var items = FindEventArray(doc.RootElement);

            return items
                .Select(ReadCalendarEvent)
                .Where(e => e is not null)
                .Cast<MacroEventModel>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load macro calendar from {Url}", url);
            return [];
        }
    }

    private bool HasTradingEconomicsCalendar()
    {
        return _options.TradingEconomics.Enabled
            && !string.IsNullOrWhiteSpace(_options.TradingEconomics.ApiKey)
            && !string.IsNullOrWhiteSpace(_options.TradingEconomics.BaseUrl);
    }

    private string BuildTradingEconomicsCalendarUrl(DateTime utcNow, TimeSpan lookAhead)
    {
        var options = _options.TradingEconomics;
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var from = utcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = utcNow.Add(lookAhead).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var countries = (options.Countries ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Uri.EscapeDataString(c.Trim().ToLowerInvariant()))
            .ToList();

        var path = countries.Count == 0
            ? $"calendar/{from}/{to}"
            : $"calendar/country/{string.Join(",", countries)}/{from}/{to}";
        var importance = Math.Clamp(options.MinImportance, 1, 3);

        return $"{baseUrl}/{path}?c={Uri.EscapeDataString(options.ApiKey!)}&importance={importance}&f=json";
    }

    private async Task<IReadOnlyList<MacroEventModel>> LoadRssAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var xml = await _http.GetStringAsync(url, cancellationToken);
            var doc = XDocument.Parse(xml);
            var rssItems = doc.Descendants("item").Select(item => ReadRssItem(item, url));
            var atomItems = doc.Descendants()
                .Where(e => e.Name.LocalName == "entry")
                .Select(item => ReadAtomItem(item, url));

            return rssItems.Concat(atomItems)
                .Where(e => e is not null)
                .Cast<MacroEventModel>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load macro RSS from {Url}", url);
            return [];
        }
    }

    private static IEnumerable<JsonElement> FindEventArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        if (root.ValueKind != JsonValueKind.Object)
            return [];

        foreach (var name in new[] { "data", "events", "calendar", "items", "result" })
        {
            if (TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray();
        }

        return [];
    }

    private static MacroEventModel? ReadCalendarEvent(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var title = ReadString(item, "event", "title", "name", "indicator", "Event");
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var country = ReadString(item, "country", "Country");
        var currency = ReadString(item, "currency", "Currency") ?? InferCurrencyFromCountry(country) ?? country;
        var occursAt = ReadDate(item, "date", "Date", "datetime", "time", "eventTime", "calendarDate");
        var impact = ReadImpact(ReadString(item, "impact", "Impact", "importance", "Importance", "volatility"));
        var kind = ClassifyKind(title, currency, null);
        var source = TryGet(item, "CalendarId", out _) ? "tradingeconomics-calendar" : ReadString(item, "source", "Source") ?? "calendar";
        var sourceId = ReadString(item, "id", "eventId", "sourceKey", "CalendarId") ?? $"{source}:{title}:{occursAt:O}:{currency}";
        var url = NormalizeTradingEconomicsUrl(ReadString(item, "url", "link", "URL"));

        return new MacroEventModel
        {
            Source = source,
            SourceKey = StableKey(sourceId),
            Kind = kind,
            Impact = impact,
            Title = Trim(title, 260) ?? title,
            Summary = Trim(BuildCalendarSummary(item), 700),
            Currency = Trim(currency?.ToUpperInvariant(), 16),
            Url = Trim(url, 1000),
            OccursAtUtc = occursAt,
        };
    }

    private static string? BuildCalendarSummary(JsonElement item)
    {
        var existing = ReadString(item, "summary", "description");
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var parts = new List<string>();
        AddPart(parts, "Country", ReadString(item, "country", "Country"));
        AddPart(parts, "Category", ReadString(item, "category", "Category"));
        AddPart(parts, "Forecast", ReadString(item, "forecast", "Forecast"));
        AddPart(parts, "TE forecast", ReadString(item, "teforecast", "TEForecast"));
        AddPart(parts, "Previous", ReadString(item, "previous", "Previous"));
        AddPart(parts, "Actual", ReadString(item, "actual", "Actual"));
        AddPart(parts, "Reference", ReadString(item, "reference", "Reference"));

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static void AddPart(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{label}: {value.Trim()}");
    }

    private static string? NormalizeTradingEconomicsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return value;

        return value.StartsWith('/')
            ? $"https://tradingeconomics.com{value}"
            : value;
    }

    private static MacroEventModel? ReadRssItem(XElement item, string sourceUrl)
    {
        var title = Value(item, "title");
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var link = Value(item, "link");
        var summary = HtmlToText(Value(item, "description"));
        var published = ParseDate(Value(item, "pubDate") ?? Value(item, "date") ?? Value(item, "updated"));
        var kind = ClassifyKind(title, null, summary);

        return new MacroEventModel
        {
            Source = Host(sourceUrl),
            SourceKey = StableKey($"{sourceUrl}:{link}:{title}:{published:O}"),
            Kind = kind,
            Impact = ClassifyImpact(title, summary),
            Title = Trim(title, 260) ?? title,
            Summary = Trim(summary, 700),
            Currency = InferCurrency(title, summary),
            Url = Trim(link, 1000),
            OccursAtUtc = published,
        };
    }

    private static MacroEventModel? ReadAtomItem(XElement item, string sourceUrl)
    {
        var title = LocalValue(item, "title");
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var link = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "link")?.Attribute("href")?.Value;
        var summary = HtmlToText(LocalValue(item, "summary") ?? LocalValue(item, "content"));
        var published = ParseDate(LocalValue(item, "published") ?? LocalValue(item, "updated"));
        var kind = ClassifyKind(title, null, summary);

        return new MacroEventModel
        {
            Source = Host(sourceUrl),
            SourceKey = StableKey($"{sourceUrl}:{link}:{title}:{published:O}"),
            Kind = kind,
            Impact = ClassifyImpact(title, summary),
            Title = Trim(title, 260) ?? title,
            Summary = Trim(summary, 700),
            Currency = InferCurrency(title, summary),
            Url = Trim(link, 1000),
            OccursAtUtc = published,
        };
    }

    private static MacroEventKind ClassifyKind(string title, string? currency, string? summary)
    {
        var text = $"{title} {currency} {summary}".ToUpperInvariant();

        if (ContainsAny(text, "FOMC", "FEDERAL RESERVE", "POWELL", "ECB", "LAGARDE", "BOE", "BOJ", "PBOC", "RATE DECISION", "INTEREST RATE"))
            return MacroEventKind.CentralBank;
        if (ContainsAny(text, "WAR", "CONFLICT", "MISSILE", "ATTACK", "INVASION", "CEASEFIRE", "GEOPOLITICAL"))
            return MacroEventKind.Geopolitical;
        if (ContainsAny(text, "SANCTION", "SEC", "CFTC", "REGULATION", "REGULATORY", "BAN", "LAWSUIT"))
            return MacroEventKind.Regulation;
        if (ContainsAny(text, "CPI", "PCE", "NFP", "PAYROLL", "GDP", "PMI", "UNEMPLOYMENT", "INFLATION", "RETAIL SALES"))
            return MacroEventKind.EconomicCalendar;

        return MacroEventKind.MarketNews;
    }

    private static MacroEventImpact ClassifyImpact(string title, string? summary)
    {
        var text = $"{title} {summary}".ToUpperInvariant();
        if (ContainsAny(text, "FOMC", "RATE DECISION", "INTEREST RATE", "CPI", "PCE", "NFP", "PAYROLL", "WAR", "SANCTION", "SEC", "ETF"))
            return MacroEventImpact.High;
        if (ContainsAny(text, "GDP", "PMI", "UNEMPLOYMENT", "INFLATION", "POWELL", "LAGARDE", "ECB", "FEDERAL RESERVE"))
            return MacroEventImpact.High;
        if (ContainsAny(text, "SPEECH", "MINUTES", "RETAIL SALES", "CLAIMS"))
            return MacroEventImpact.Medium;
        return MacroEventImpact.Low;
    }

    private static MacroEventImpact ReadImpact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MacroEventImpact.Medium;

        var text = value.Trim();
        if (int.TryParse(text, out var numeric))
            return numeric >= 4 ? MacroEventImpact.Critical : numeric >= 3 ? MacroEventImpact.High : numeric == 2 ? MacroEventImpact.Medium : MacroEventImpact.Low;

        return text.ToLowerInvariant() switch
        {
            "critical" or "very high" => MacroEventImpact.Critical,
            "high" or "important" => MacroEventImpact.High,
            "medium" or "moderate" => MacroEventImpact.Medium,
            _ => MacroEventImpact.Low,
        };
    }

    private static DateTime? ReadDate(JsonElement item, params string[] names)
    {
        var raw = ReadString(item, names);
        return ParseDate(raw);
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(item, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return null;
    }

    private static bool TryGet(JsonElement item, string name, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? Value(XElement item, string name)
    {
        return item.Element(name)?.Value ?? LocalValue(item, name);
    }

    private static string? LocalValue(XElement item, string name)
    {
        return item.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    }

    private static string? InferCurrency(string title, string? summary)
    {
        var text = $"{title} {summary}".ToUpperInvariant();
        if (ContainsAny(text, "FED", "FOMC", "USD", "UNITED STATES", "US "))
            return "USD";
        if (ContainsAny(text, "ECB", "EURO", "EUR"))
            return "EUR";
        if (ContainsAny(text, "BOJ", "JAPAN", "JPY"))
            return "JPY";
        if (ContainsAny(text, "BOE", "UK ", "GBP"))
            return "GBP";
        return "GLOBAL";
    }

    private static string? InferCurrencyFromCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return null;

        return country.Trim().ToUpperInvariant() switch
        {
            "UNITED STATES" or "US" or "USA" => "USD",
            "EURO AREA" or "EUROZONE" or "EUROPEAN UNION" => "EUR",
            "UNITED KINGDOM" or "UK" or "GREAT BRITAIN" => "GBP",
            "JAPAN" => "JPY",
            "CHINA" => "CNY",
            "CANADA" => "CAD",
            "AUSTRALIA" => "AUD",
            "NEW ZEALAND" => "NZD",
            "SWITZERLAND" => "CHF",
            _ => null,
        };
    }

    private static string HtmlToText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return Regex.Replace(value, "<.*?>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Trim();
    }

    private static string StableKey(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static string Host(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "rss";
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
