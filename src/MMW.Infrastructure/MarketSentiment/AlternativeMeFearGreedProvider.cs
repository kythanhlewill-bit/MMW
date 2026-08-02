using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MMW.Application.MarketData;

namespace MMW.Infrastructure.MarketSentiment;

/// <summary>Chỉ số sợ hãi/tham lam từ alternative.me — công khai, miễn phí, không cần khoá (R-004).</summary>
public sealed class AlternativeMeFearGreedProvider : IMarketSentimentProvider
{
    private const string Endpoint = "https://api.alternative.me/fng/?limit=1";

    private readonly HttpClient _http;
    private readonly ILogger<AlternativeMeFearGreedProvider> _log;

    public AlternativeMeFearGreedProvider(HttpClient http, ILogger<AlternativeMeFearGreedProvider> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(Endpoint, ct);
            var value = ParseIndex(json);

            if (value is null)
                _log.LogWarning("Không bóc tách được chỉ số Fear & Greed từ alternative.me.");

            return value;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nuốt lỗi có kèm ghi vết: nguồn này chỉ đóng góp một phần nhỏ vào phân loại
            // trạng thái ngày, nên nó chết không được phép kéo cả kế hoạch ngày chết theo.
            _log.LogWarning(ex, "Không lấy được chỉ số Fear & Greed từ alternative.me.");
            return null;
        }
    }

    /// <summary>
    /// Bóc tách phản hồi. Trả <c>null</c> với mọi dạng hỏng.
    /// </summary>
    /// <remarks>
    /// Hai chi tiết của định dạng này dễ làm sai, đã kiểm chứng ở T001:
    /// <c>value</c> là <b>chuỗi</b> chứ không phải số, và <c>timestamp</c> tính bằng
    /// <b>giây</b> chứ không phải mili-giây.
    /// </remarks>
    public static int? ParseIndex(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0)
            {
                return null;
            }

            if (!data[0].TryGetProperty("value", out var v)) return null;

            var raw = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.ToString(),
                _ => null,
            };

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return null;

            return index is < 0 or > 100 ? null : index;
        }
        catch (JsonException) { return null; }
    }
}
