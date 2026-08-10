using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

[Authorize]
public sealed class ShadowComparisonController : Controller
{
    private readonly IBaseRepository<AiSignalScanRecord> _aiAudits;
    private readonly IBaseRepository<EntryScorecard> _scorecards;
    private readonly IBaseRepository<MarketSnapshot> _snapshots;

    public ShadowComparisonController(
        IBaseRepository<AiSignalScanRecord> aiAudits,
        IBaseRepository<EntryScorecard> scorecards,
        IBaseRepository<MarketSnapshot> snapshots)
    {
        _aiAudits = aiAudits;
        _scorecards = scorecards;
        _snapshots = snapshots;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var to = NormalizeUtc(toUtc ?? DateTime.UtcNow);
        var from = NormalizeUtc(fromUtc ?? to.AddDays(-30));
        if (from > to) (from, to) = (to, from);

        var audits = await _aiAudits.Queryable.AsNoTracking()
            .Where(x => x.ScannedAt >= from && x.ScannedAt <= to)
            .OrderByDescending(x => x.ScannedAt)
            .ToListAsync(ct);
        var deterministicProposalCount = await _scorecards.Queryable.AsNoTracking()
            .CountAsync(x => !x.IsBacktest && x.EvaluatedAtUtc >= from && x.EvaluatedAtUtc <= to
                && x.Outcome == ScorecardOutcome.Entered, ct);

        var proposals = audits
            .Where(IsAiProposal)
            .Take(200)
            .ToList();
        var symbols = proposals.Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var snapshots = await _snapshots.Queryable.AsNoTracking()
            .Where(x => symbols.Contains(x.Symbol))
            .ToListAsync(ct);
        var latestPrices = snapshots
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First().Price,
                StringComparer.OrdinalIgnoreCase);

        var model = new ShadowComparisonViewModel
        {
            FromUtc = from,
            ToUtc = to,
            AiScanCount = audits.Count,
            AiProposalCount = audits.Count(IsAiProposal),
            DeterministicProposalCount = deterministicProposalCount,
            ComparableCount = audits.Count(x => x.IsDisagreement.HasValue),
            DisagreementCount = audits.Count(x => x.IsDisagreement == true),
            AiProposals = proposals.Select(x => BuildRow(x, latestPrices)).ToList(),
        };

        return View(model);
    }

    private static bool IsAiProposal(AiSignalScanRecord x) =>
        x.Status == "Accepted" && x.Action is "long" or "short";

    private static ShadowProposalRow BuildRow(
        AiSignalScanRecord audit, IReadOnlyDictionary<string, decimal> prices)
    {
        if (!prices.TryGetValue(audit.Symbol, out var currentPrice)
            || audit.Entry is not > 0m || audit.StopLoss is not > 0m || audit.TakeProfit is not > 0m)
            return new ShadowProposalRow { Audit = audit };

        var risk = Math.Abs(audit.Entry.Value - audit.StopLoss.Value);
        if (risk == 0m) return new ShadowProposalRow { Audit = audit, CurrentPrice = currentPrice };

        var isLong = audit.Action == "long";
        var resultR = isLong
            ? (currentPrice - audit.Entry.Value) / risk
            : (audit.Entry.Value - currentPrice) / risk;
        var outcome = isLong
            ? currentPrice >= audit.TakeProfit.Value ? "Chạm TP theo giá hiện tại"
                : currentPrice <= audit.StopLoss.Value ? "Chạm SL theo giá hiện tại" : "Đang mở giả định"
            : currentPrice <= audit.TakeProfit.Value ? "Chạm TP theo giá hiện tại"
                : currentPrice >= audit.StopLoss.Value ? "Chạm SL theo giá hiện tại" : "Đang mở giả định";

        return new ShadowProposalRow
        {
            Audit = audit,
            CurrentPrice = currentPrice,
            HypotheticalResultR = Math.Round(resultR, 4),
            HypotheticalOutcome = outcome,
        };
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
