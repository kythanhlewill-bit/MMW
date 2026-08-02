using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;

namespace MMW.Infrastructure.Ai;

/// <summary>
/// DeepSeek (OpenAI-compatible /chat/completions, Bearer auth).
/// BaseUrl: https://api.deepseek.com · Model: deepseek-chat / deepseek-reasoner.
/// </summary>
public class DeepSeekLlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<DeepSeekLlmService> _logger;

    public DeepSeekLlmService(HttpClient http, IOptions<LlmOptions> options, ILogger<DeepSeekLlmService> logger)
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
                Model = string.IsNullOrWhiteSpace(_options.Model) ? "deepseek-chat" : _options.Model,
                Messages =
                [
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userMessage },
                ],
                Temperature = 0.3m,
                MaxTokens = _options.MaxOutputTokens > 0 ? _options.MaxOutputTokens : 2048,
            };

            using var response = await _http.PostAsJsonAsync("chat/completions", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("DeepSeek API trả {StatusCode}: {Body}", response.StatusCode, body);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
            return result?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeepSeek call failed");
            return null;
        }
    }

    #region DTOs

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public decimal Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
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
