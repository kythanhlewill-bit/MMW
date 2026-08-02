using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface IMacroEventService
{
    Task<MacroEventContext> GetContextForTradeAsync(
        string symbol,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<int> ScanAndNotifyAsync(CancellationToken cancellationToken = default);
}
