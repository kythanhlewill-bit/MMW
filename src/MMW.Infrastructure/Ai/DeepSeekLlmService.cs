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
            var choice = result?.Choices?.FirstOrDefault();
            var content = choice?.Message?.Content;

            // Phản hồi 200 nhưng content rỗng là trạng thái ĐẮT: bên gọi coi như hỏng rồi gọi lại,
            // nên một lần im lặng thành hai lần trả tiền. Đo được 62% lượt quét rơi vào đây sau khi
            // đổi sang model có suy luận, vì vậy nghi phạm số một là phần suy luận ăn hết max_tokens
            // (finish_reason = "length", chữ nằm ở reasoning_content chứ không phải content).
            // Ghi rõ hai trường đó ra log — không có chúng thì chỉ biết "rỗng" mà không biết vì sao.
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning(
                    "DeepSeek trả nội dung rỗng (model {Model}, finish_reason {Finish}, reasoning {ReasoningLen} ký tự, max_tokens {MaxTokens}). "
                    + "Bên gọi sẽ coi là lỗi và gọi lại — mỗi lần như vậy tốn gấp đôi.",
                    request.Model, choice?.FinishReason ?? "(không có)",
                    choice?.Message?.ReasoningContent?.Length ?? 0, request.MaxTokens);
            }

            return content;
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

        /// <summary>
        /// Model có suy luận trả phần nghĩ ở đây, tách khỏi <see cref="Content"/>. Chỉ đọc để
        /// chẩn đoán — không bao giờ dùng làm câu trả lời, vì nó là nháp chứ không phải kết quả.
        /// </summary>
        [JsonPropertyName("reasoning_content")] public string? ReasoningContent { get; set; }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    #endregion
}
