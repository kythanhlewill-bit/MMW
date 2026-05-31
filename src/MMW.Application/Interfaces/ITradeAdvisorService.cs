namespace MMW.Application.Interfaces;

public interface ITradeAdvisorService
{
    Task<int> AnalyzeOpenTradesAsync(CancellationToken cancellationToken = default);
}
