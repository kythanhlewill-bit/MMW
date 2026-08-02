using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface IMacroEventProvider
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<MacroEventModel>> GetEventsAsync(
        DateTime utcNow,
        TimeSpan lookAhead,
        TimeSpan newsLookBack,
        CancellationToken cancellationToken = default);
}
