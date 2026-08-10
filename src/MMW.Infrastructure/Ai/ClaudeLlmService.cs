using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;

namespace MMW.Infrastructure.Ai;

/// <summary>Anthropic Messages API adapter. API key chỉ đọc qua cấu hình/User Secrets.</summary>
public sealed class ClaudeLlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<ClaudeLlmService> _logger;

    public ClaudeLlmService(HttpClient http, IOptions<LlmOptions> options, ILogger<ClaudeLlmService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string?> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var request = new MessageRequest
            {
                Model = string.IsNullOrWhiteSpace(_options.Model) ? "claude-sonnet-4-20250514" : _options.Model,
                System = systemPrompt,
                MaxTokens = _options.MaxOutputTokens > 0 ? _options.MaxOutputTokens : 2048,
                Messages = [new Message { Role = "user", Content = userMessage }],
            };

            using var response = await _http.PostAsJsonAsync("v1/messages", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Claude API trả {StatusCode}: {Body}", response.StatusCode,
                    await response.Content.ReadAsStringAsync(ct));
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<MessageResponse>(ct);
            return result?.Content?.FirstOrDefault(x => x.Type == "text")?.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Claude call failed");
            return null;
        }
    }

    private sealed class MessageRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("system")] public string System { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("messages")] public List<Message> Messages { get; set; } = [];
    }

    private sealed class Message
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
