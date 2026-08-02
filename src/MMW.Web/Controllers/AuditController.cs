using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Shared.Models;
using MMW.Web.Helpers;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class AuditController : Controller
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    private readonly IBaseRepository<AiSignalScanRecord> _aiAudits;
    private readonly IBaseRepository<ExchangeApiAuditRecord> _exchangeAudits;

    public AuditController(
        IBaseRepository<AiSignalScanRecord> aiAudits,
        IBaseRepository<ExchangeApiAuditRecord> exchangeAudits)
    {
        _aiAudits = aiAudits;
        _exchangeAudits = exchangeAudits;
    }

    public async Task<IActionResult> Index(
        string tab = "ai",
        string? aiSearch = null,
        string? aiSymbol = null,
        string? aiStatus = null,
        string? aiAction = null,
        DateTime? aiFrom = null,
        DateTime? aiTo = null,
        string aiSort = "scanned_desc",
        int aiPage = 1,
        int aiPageSize = 20,
        string? exSearch = null,
        string? exSymbol = null,
        string? exMethod = null,
        bool? exSucceeded = null,
        DateTime? exFrom = null,
        DateTime? exTo = null,
        string exSort = "requested_desc",
        int exPage = 1,
        int exPageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var aiQuery = new AiAuditQuery
        {
            Search = aiSearch,
            Symbol = NormalizeSymbol(aiSymbol),
            Status = Normalize(aiStatus),
            Action = Normalize(aiAction),
            From = aiFrom ?? VietnamTimeHelper.VietnamNow().AddHours(-24),   // mặc định 24h gần nhất (giờ VN)
            To = aiTo ?? VietnamTimeHelper.VietnamNow(),
            Sort = Normalize(aiSort) ?? "scanned_desc",
            Page = NormalizePage(aiPage),
            PageSize = NormalizePageSize(aiPageSize),
        };

        var exchangeQuery = new ExchangeAuditQuery
        {
            Search = exSearch,
            Symbol = NormalizeSymbol(exSymbol),
            Method = Normalize(exMethod)?.ToUpperInvariant(),
            Succeeded = exSucceeded,
            From = exFrom ?? VietnamTimeHelper.VietnamNow().AddHours(-24),   // mặc định 24h gần nhất (giờ VN)
            To = exTo ?? VietnamTimeHelper.VietnamNow(),
            Sort = Normalize(exSort) ?? "requested_desc",
            Page = NormalizePage(exPage),
            PageSize = NormalizePageSize(exPageSize),
        };

        var model = new AuditIndexViewModel
        {
            ActiveTab = string.Equals(tab, "exchange", StringComparison.OrdinalIgnoreCase) ? "exchange" : "ai",
            AiQuery = aiQuery,
            ExchangeQuery = exchangeQuery,
            AiRecords = await GetAiAuditsAsync(aiQuery, cancellationToken),
            ExchangeRecords = await GetExchangeAuditsAsync(exchangeQuery, cancellationToken),
        };

        return View(model);
    }

    private async Task<PaginatedResult<AiSignalScanRecord>> GetAiAuditsAsync(AiAuditQuery filter, CancellationToken ct)
    {
        var query = _aiAudits.GetAll();

        if (!string.IsNullOrWhiteSpace(filter.Symbol))
            query = query.Where(x => x.Symbol == filter.Symbol);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(x => x.Action == filter.Action);
        if (filter.From is DateTime from)
        {
            var fromUtc = VietnamTimeHelper.ToUtc(from);
            query = query.Where(x => x.ScannedAt >= fromUtc);
        }
        if (filter.To is DateTime to)
        {
            var toUtc = VietnamTimeHelper.ToUtc(to);
            query = query.Where(x => x.ScannedAt <= toUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Symbol.Contains(term) ||
                (x.Status != null && x.Status.Contains(term)) ||
                (x.Action != null && x.Action.Contains(term)) ||
                (x.RejectReason != null && x.RejectReason.Contains(term)) ||
                (x.AiReason != null && x.AiReason.Contains(term)) ||
                (x.RequestJson != null && x.RequestJson.Contains(term)) ||
                (x.ResponseJson != null && x.ResponseJson.Contains(term)));
        }

        var count = await query.CountAsync(ct);
        query = filter.Sort switch
        {
            "scanned_asc" => query.OrderBy(x => x.ScannedAt).ThenBy(x => x.Id),
            "symbol_asc" => query.OrderBy(x => x.Symbol).ThenByDescending(x => x.ScannedAt),
            "symbol_desc" => query.OrderByDescending(x => x.Symbol).ThenByDescending(x => x.ScannedAt),
            "status_asc" => query.OrderBy(x => x.Status).ThenByDescending(x => x.ScannedAt),
            "status_desc" => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.ScannedAt),
            "score_asc" => query.OrderBy(x => x.Score).ThenByDescending(x => x.ScannedAt),
            "score_desc" => query.OrderByDescending(x => x.Score).ThenByDescending(x => x.ScannedAt),
            _ => query.OrderByDescending(x => x.ScannedAt).ThenByDescending(x => x.Id),
        };

        var data = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return PaginatedResult<AiSignalScanRecord>.Create(data, count, filter.Page, filter.PageSize);
    }

    private async Task<PaginatedResult<ExchangeApiAuditRecord>> GetExchangeAuditsAsync(ExchangeAuditQuery filter, CancellationToken ct)
    {
        var query = _exchangeAudits.GetAll();

        if (!string.IsNullOrWhiteSpace(filter.Symbol))
            query = query.Where(x => x.Symbol == filter.Symbol);
        if (!string.IsNullOrWhiteSpace(filter.Method))
            query = query.Where(x => x.Method == filter.Method);
        if (filter.Succeeded.HasValue)
            query = query.Where(x => x.Succeeded == filter.Succeeded.Value);
        if (filter.From is DateTime from)
        {
            var fromUtc = VietnamTimeHelper.ToUtc(from);
            query = query.Where(x => x.RequestedAtUtc >= fromUtc);
        }
        if (filter.To is DateTime to)
        {
            var toUtc = VietnamTimeHelper.ToUtc(to);
            query = query.Where(x => x.RequestedAtUtc <= toUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Exchange.Contains(term) ||
                (x.Symbol != null && x.Symbol.Contains(term)) ||
                x.Method.Contains(term) ||
                x.Path.Contains(term) ||
                (x.ClientOrderId != null && x.ClientOrderId.Contains(term)) ||
                (x.Error != null && x.Error.Contains(term)) ||
                (x.RequestJson != null && x.RequestJson.Contains(term)) ||
                (x.ResponseJson != null && x.ResponseJson.Contains(term)));
        }

        var count = await query.CountAsync(ct);
        query = filter.Sort switch
        {
            "requested_asc" => query.OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id),
            "symbol_asc" => query.OrderBy(x => x.Symbol).ThenByDescending(x => x.RequestedAtUtc),
            "symbol_desc" => query.OrderByDescending(x => x.Symbol).ThenByDescending(x => x.RequestedAtUtc),
            "status_asc" => query.OrderBy(x => x.StatusCode).ThenByDescending(x => x.RequestedAtUtc),
            "status_desc" => query.OrderByDescending(x => x.StatusCode).ThenByDescending(x => x.RequestedAtUtc),
            "duration_asc" => query.OrderBy(x => x.DurationMs).ThenByDescending(x => x.RequestedAtUtc),
            "duration_desc" => query.OrderByDescending(x => x.DurationMs).ThenByDescending(x => x.RequestedAtUtc),
            _ => query.OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id),
        };

        var data = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return PaginatedResult<ExchangeApiAuditRecord>.Create(data, count, filter.Page, filter.PageSize);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSymbol(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) =>
        AllowedPageSizes.Contains(pageSize) ? pageSize : 20;
}
