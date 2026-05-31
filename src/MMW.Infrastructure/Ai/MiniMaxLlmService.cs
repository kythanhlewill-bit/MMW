using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;

namespace MMW.Infrastructure.Ai;

public class MiniMaxLlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<MiniMaxLlmService> _logger;

    public MiniMaxLlmService(HttpClient http, IOptions<LlmOptions> options, ILogger<MiniMaxLlmService> logger)
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
            var request = new ChatRequest
            {
                Model = _options.Model,
                Messages =
                [
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userMessage },
                ],
                Temperature = 0.3m,
            };

            using var response = await _http.PostAsJsonAsync("v1/chat/completions", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
            return result?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM call failed");
            return null;
        }
    }

    #region DTOs

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public decimal Temperature { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    #endregion
}
