using System.Text.Json;

namespace MMW.Application.Ai;

/// <summary>
/// Đọc JSON từ phản hồi của mô hình ngôn ngữ, và soi khoá gợi ý lệnh.
/// </summary>
/// <remarks>
/// Tách riêng vì hai bộ kiểm chứng dùng chung, và vì phần "sửa một lần" dễ bị viết lệch nhau
/// nếu nhân đôi: một bên chấp nhận khối mã bọc, bên kia không, và sự khác biệt đó chỉ lộ ra
/// khi nhà cung cấp mô hình đổi định dạng đầu ra.
/// </remarks>
internal static class AiJson
{
    /// <summary>
    /// Các khoá cho thấy phản hồi đã trôi khỏi vai trò "chỉ mô tả bối cảnh".
    /// </summary>
    /// <remarks>
    /// So khớp sau khi bỏ hết ký tự không phải chữ-số và hạ chữ thường, nên
    /// <c>stop_loss</c>, <c>stopLoss</c> và <c>Stop-Loss</c> đều bị bắt bằng một mục.
    /// </remarks>
    private static readonly string[] TradeSuggestingKeys =
        ["entry", "stoploss", "takeprofit", "direction", "side", "action"];

    /// <summary>Đọc JSON, cho phép ĐÚNG MỘT lần thử sửa bằng cách cắt lấy khối ngoài cùng.</summary>
    public static JsonDocument? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var direct = Parse(raw);
        if (direct is not null) return direct;

        // Một lần sửa duy nhất: mô hình hay bọc JSON trong khối mã hoặc kèm lời dẫn.
        // Nhiều hơn một lần là bắt đầu đoán ý, và đoán ý một phản hồi hỏng thì kết quả
        // cũng hỏng — chỉ là hỏng theo cách khó phát hiện hơn.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        return start >= 0 && end > start ? Parse(raw[start..(end + 1)]) : null;
    }

    /// <summary>Mọi khoá gợi ý lệnh tìm thấy ở BẤT KỲ độ sâu nào của cây JSON.</summary>
    public static IReadOnlyList<string> FindTradeSuggestingKeys(JsonElement root)
    {
        var found = new List<string>();
        Walk(root, found);
        return found;
    }

    private static void Walk(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (TradeSuggestingKeys.Contains(Canonical(property.Name), StringComparer.Ordinal))
                        found.Add(property.Name);

                    Walk(property.Value, found);
                }
                break;

            case JsonValueKind.Array:
                // Lồng trong mảng là đường lách hiển nhiên nhất: symbolNotes[0].entry.
                foreach (var item in element.EnumerateArray()) Walk(item, found);
                break;
        }
    }

    private static string Canonical(string key) =>
        new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static JsonDocument? Parse(string text)
    {
        try
        {
            var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Đọc trường an toàn: kiểu sai coi như thiếu, không ném ────────────────

    public static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static decimal? DecimalOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
                ? number
                : null;

    public static bool BoolOrDefault(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static IReadOnlyList<string> StringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }
}
