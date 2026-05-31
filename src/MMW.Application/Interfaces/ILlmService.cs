namespace MMW.Application.Interfaces;

public interface ILlmService
{
    bool IsConfigured { get; }
    Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
