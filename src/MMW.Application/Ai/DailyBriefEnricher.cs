using MMW.Domain.Entities;

namespace MMW.Application.Ai;

public interface IDailyBriefEnricher
{
    Task<int> EnrichAsync(DailyPlan completedPlan, CancellationToken ct = default);
}

public sealed class DailyBriefEnricher : IDailyBriefEnricher
{
    private readonly IMarketContextService _context;

    public DailyBriefEnricher(IMarketContextService context) => _context = context;

    public Task<int> EnrichAsync(DailyPlan completedPlan, CancellationToken ct = default) =>
        _context.RunDailyBriefAsync(completedPlan, ct);
}
