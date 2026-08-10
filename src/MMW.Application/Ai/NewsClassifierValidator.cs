using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Ai;

public interface INewsClassifierValidator
{
    NewsClassification Validate(string? rawResponse, IReadOnlyList<string> watchedSymbols, EngineSetting settings);
}

public sealed class NewsClassifierValidator : INewsClassifierValidator
{
    public NewsClassification Validate(
        string? rawResponse, IReadOnlyList<string> watchedSymbols, EngineSetting settings)
    {
        using var document = AiJson.TryParse(rawResponse);
        if (document is null) return NewsClassification.Neutral("json: không đọc được phản hồi");

        var root = document.RootElement;
        var tradeKeys = AiJson.FindTradeSuggestingKeys(root);
        if (tradeKeys.Count > 0)
            return NewsClassification.Neutral(tradeKeys.Select(k => $"{k}: khoá gợi ý lệnh").ToArray());

        var rejected = new List<string>();
        var rawSeverity = AiJson.StringOrNull(root, "severity");
        var severity = ContextSeverity.Normalize(rawSeverity);
        if (!string.Equals(severity, rawSeverity?.Trim(), StringComparison.OrdinalIgnoreCase))
            rejected.Add($"severity: '{rawSeverity}' không thuộc bảng, hạ về noise");

        var rumor = AiJson.BoolOrDefault(root, "isRumor");
        if (rumor && ContextSeverity.Rank(severity) > ContextSeverity.Rank(ContextSeverity.Medium))
        {
            rejected.Add($"severity: {severity} → medium vì là tin đồn");
            severity = ContextSeverity.Medium;
        }

        var givenHalfLife = AiJson.DecimalOrNull(root, "halfLifeMinutes");
        var halfLife = givenHalfLife is null
            ? Math.Clamp(settings.AiContextDefaultTtlMinutes, 0, 1440)
            : Math.Clamp((int)decimal.Truncate(givenHalfLife.Value), 0, 1440);
        if (givenHalfLife is not null && halfLife != givenHalfLife.Value)
            rejected.Add($"halfLifeMinutes: {givenHalfLife} → {halfLife}");

        var watched = watchedSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var supplied = AiJson.StringArray(root, "affectedSymbols")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var symbols = supplied.Where(watched.Contains).ToList();
        foreach (var removed in supplied.Except(symbols, StringComparer.Ordinal))
            rejected.Add($"affectedSymbols: loại '{removed}' vì không được theo dõi");

        var leaning = AiJson.StringOrNull(root, "leaning")?.Trim().ToLowerInvariant() switch
        {
            "bullish" => MarketBias.Bullish,
            "bearish" => MarketBias.Bearish,
            _ => MarketBias.Neutral,
        };

        return new NewsClassification
        {
            Accepted = true,
            Severity = severity,
            Leaning = leaning,
            AffectedSymbols = symbols,
            HalfLifeMinutes = halfLife,
            IsRumor = rumor,
            RejectedFields = rejected,
        };
    }
}
