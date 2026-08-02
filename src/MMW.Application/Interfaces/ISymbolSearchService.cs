namespace MMW.Application.Interfaces;

public interface ISymbolSearchService
{
    Task<IReadOnlyList<string>> SearchFuturesSymbolsAsync(string? term, int take = 30, CancellationToken cancellationToken = default);
}
