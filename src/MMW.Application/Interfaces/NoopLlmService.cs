namespace MMW.Application.Interfaces;

/// <summary>Mặc định trung tính để toàn bộ engine vẫn resolve và chạy khi chưa cấu hình AI.</summary>
public sealed class NoopLlmService : ILlmService
{
    public bool IsConfigured => false;

    public Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
