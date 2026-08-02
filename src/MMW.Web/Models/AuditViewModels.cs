using MMW.Domain.Entities;
using MMW.Shared.Models;

namespace MMW.Web.Models;

public class AuditIndexViewModel
{
    public string ActiveTab { get; set; } = "ai";
    public AiAuditQuery AiQuery { get; set; } = new();
    public ExchangeAuditQuery ExchangeQuery { get; set; } = new();
    public PaginatedResult<AiSignalScanRecord> AiRecords { get; set; } = new(true, new List<AiSignalScanRecord>(), null, 0, 1, 20);
    public PaginatedResult<ExchangeApiAuditRecord> ExchangeRecords { get; set; } = new(true, new List<ExchangeApiAuditRecord>(), null, 0, 1, 20);
}

public class AiAuditQuery
{
    public string? Search { get; set; }
    public string? Symbol { get; set; }
    public string? Status { get; set; }
    public string? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Sort { get; set; } = "scanned_desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ExchangeAuditQuery
{
    public string? Search { get; set; }
    public string? Symbol { get; set; }
    public string? Method { get; set; }
    public bool? Succeeded { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Sort { get; set; } = "requested_desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
