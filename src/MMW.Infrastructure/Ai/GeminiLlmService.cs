using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;

namespace MMW.Infrastructure.Ai;

public class GeminiLlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<GeminiLlmService> _logger;

    public GeminiLlmService(HttpClient http, IOptions<LlmOptions> options, ILogger<GeminiLlmService> logger)
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
            var request = new GenerateContentRequest
            {
                SystemInstruction = new GeminiContent
                {
                    Parts = [new GeminiPart { Text = systemPrompt }],
                },
                Contents =
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts = [new GeminiPart { Text = userMessage }],
                    },
                ],
                GenerationConfig = new GenerationConfig
                {
                    Temperature = 0.2m,
                    MaxOutputTokens = ResolveMaxOutputTokens(),
                    ResponseMimeType = WantsJsonResponse(systemPrompt) ? "application/json" : null,
                    ResponseSchema = WantsJsonResponse(systemPrompt) ? GeminiSchema.PreflightReview : null,
                },
            };

            using var response = await SendGenerateRequestAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (request.GenerationConfig.ResponseSchema is not null)
                {
                    _logger.LogWarning("Gemini API returned {StatusCode} with response schema. Retrying without schema.", response.StatusCode);
                    request.GenerationConfig.ResponseSchema = null;

                    using var retryResponse = await SendGenerateRequestAsync(request, ct);
                    if (!retryResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Gemini API returned {StatusCode}", retryResponse.StatusCode);
                        return null;
                    }

                    var retryResult = await retryResponse.Content.ReadFromJsonAsync<GenerateContentResponse>(ct);
                    return ExtractText(retryResult);
                }

                _logger.LogWarning("Gemini API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(ct);
            return ExtractText(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini API call failed");
            return null;
        }
    }

    private string BuildEndpoint()
    {
        var model = string.IsNullOrWhiteSpace(_options.Model)
            ? "gemini-2.5-flash"
            : _options.Model.Trim();

        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];

        return $"v1beta/models/{model}:generateContent";
    }

    private async Task<HttpResponseMessage> SendGenerateRequestAsync(GenerateContentRequest request, CancellationToken ct)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
        httpRequest.Headers.Add("x-goog-api-key", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(request);
        return await _http.SendAsync(httpRequest, ct);
    }

    private static string? ExtractText(GenerateContentResponse? result)
    {
        return result?.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Select(p => p.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    private static bool WantsJsonResponse(string systemPrompt)
    {
        return systemPrompt.Contains("JSON", StringComparison.OrdinalIgnoreCase)
            || systemPrompt.Contains("Schema", StringComparison.OrdinalIgnoreCase);
    }

    private int ResolveMaxOutputTokens()
    {
        return _options.MaxOutputTokens > 0 ? _options.MaxOutputTokens : 2048;
    }

    private sealed class GenerateContentRequest
    {
        [JsonPropertyName("systemInstruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GenerationConfig GenerationConfig { get; set; } = new();
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class GenerationConfig
    {
        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("responseMimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ResponseMimeType { get; set; }

        [JsonPropertyName("responseSchema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ResponseSchema { get; set; }
    }

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private static class GeminiSchema
    {
        public static readonly object PreflightReview = new
        {
            type = "OBJECT",
            properties = new
            {
                decision = new { type = "STRING", @enum = new[] { "accept", "reject", "wait" } },
                score = new { type = "INTEGER" },
                confidence = new { type = "NUMBER" },
                advice = new { type = "STRING" },
                reasons = new { type = "ARRAY", items = new { type = "STRING" } },
                riskWarnings = new { type = "ARRAY", items = new { type = "STRING" } },
                invalidation = new { type = "STRING" },
            },
            required = new[] { "decision", "score", "confidence", "advice", "reasons", "riskWarnings", "invalidation" },
        };
    }
}
