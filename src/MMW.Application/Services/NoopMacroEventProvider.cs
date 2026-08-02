using MMW.Application.Interfaces;
using MMW.Application.Models;

namespace MMW.Application.Services;

public class NoopMacroEventProvider : IMacroEventProvider
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<MacroEventModel>> GetEventsAsync(
        DateTime utcNow,
        TimeSpan lookAhead,
        TimeSpan newsLookBack,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MacroEventModel>>([]);
    }
}
