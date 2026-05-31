namespace MMW.Application.Interfaces;

public sealed record ImportResult(int Imported, int Skipped);

public interface IMarketImportService
{
    /// <summary>
    /// Import lịch sử khớp lệnh (fill) của một symbol từ sàn vào journal (Source=Import).
    /// Chống trùng bằng ExternalId. LƯU Ý: đây là fill thô, chưa ghép thành lệnh round-trip.
    /// </summary>
    Task<ImportResult> ImportTradesAsync(long accountId, string symbol, int limit = 500, CancellationToken cancellationToken = default);
}
