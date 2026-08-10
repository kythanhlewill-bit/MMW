using MMW.Domain.Entities;

namespace MMW.Web.Models;

public sealed class ShadowComparisonViewModel
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int AiScanCount { get; init; }
    public int AiProposalCount { get; init; }
    public int DeterministicProposalCount { get; init; }
    public int ComparableCount { get; init; }
    public int DisagreementCount { get; init; }
    public IReadOnlyList<ShadowProposalRow> AiProposals { get; init; } = [];

    public decimal? DisagreementRate => ComparableCount == 0
        ? null
        : Math.Round(100m * DisagreementCount / ComparableCount, 2);
}

public sealed class ShadowProposalRow
{
    public required AiSignalScanRecord Audit { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? HypotheticalResultR { get; init; }
    public string HypotheticalOutcome { get; init; } = "Chưa có giá";
}
