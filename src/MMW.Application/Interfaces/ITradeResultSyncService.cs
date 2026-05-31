namespace MMW.Application.Interfaces;

public interface ITradeResultSyncService
{
    Task<SyncResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default);
    Task<SyncResult> SyncAccountAsync(long accountId, CancellationToken cancellationToken = default);
}

public sealed record SyncResult(int Synced, int Failed, int Skipped);
