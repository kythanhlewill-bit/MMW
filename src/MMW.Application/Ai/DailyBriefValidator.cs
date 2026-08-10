using System.Globalization;
using System.Text.Json;
using MMW.Domain.Entities;

namespace MMW.Application.Ai;

public interface IDailyBriefValidator
{
    /// <summary>
    /// Soi phản hồi Daily Brief theo sáu bước của hợp đồng. Không bao giờ ném:
    /// mọi đầu vào hỏng đều quy về bối cảnh trung tính.
    /// </summary>
    DailyBriefResult Validate(
        string? rawResponse,
        IReadOnlyList<ScheduledEvent> providedCalendar,
        DateTime utcNow,
        EngineSetting settings);
}

/// <summary>
/// Sáu bước kiểm chứng phía nhận của Daily Brief.
/// </summary>
/// <remarks>
/// Lời nhắc là một yêu cầu lịch sự; lớp này là hàng rào. Nó được viết với giả định rằng
/// toàn bộ lời nhắc đã bị phớt lờ — giả định đúng cho một thành phần mà ta không kiểm soát
/// được đầu ra và không kiểm soát được cả thời điểm nhà cung cấp đổi mô hình bên dưới.
/// </remarks>
public sealed class DailyBriefValidator : IDailyBriefValidator
{
    /// <summary>Trần độ tin cậy. AI tự khai bao nhiêu cũng không vượt được mức này (FR-043).</summary>
    private const decimal ConfidenceCeiling = 0.8m;

    /// <summary>Giới hạn tầm nhìn của cửa sổ chặn do AI đề xuất.</summary>
    private static readonly TimeSpan ProposalHorizon = TimeSpan.FromHours(48);

    private const int NarrativeMaxLength = 300;

    private static readonly string[] DayRiskLevels = ["low", "normal", "elevated", "extreme"];

    public DailyBriefResult Validate(
        string? rawResponse,
        IReadOnlyList<ScheduledEvent> providedCalendar,
        DateTime utcNow,
        EngineSetting settings)
    {
        // Bước 6 — JSON không đọc được sau một lần thử sửa ⟹ bối cảnh trung tính.
        using var document = AiJson.TryParse(rawResponse);
        if (document is null) return DailyBriefResult.Neutral("json: không đọc được phản hồi");

        var root = document.RootElement;

        // Bước 5 — chạy TRƯỚC mọi bước khác và loại TOÀN BỘ phản hồi. Một phản hồi cố đưa ra
        // tín hiệu giao dịch là dấu hiệu lời nhắc đã trôi khỏi vai trò, và khi vai trò đã trôi
        // thì phần còn lại của phản hồi cũng không đáng tin.
        var tradeKeys = AiJson.FindTradeSuggestingKeys(root);
        if (tradeKeys.Count > 0)
            return DailyBriefResult.Neutral(tradeKeys.Select(k => $"{k}: khoá gợi ý lệnh").ToArray());

        var rejected = new List<string>();

        // Bước 1 — trần độ tin cậy.
        decimal? confidence = null;
        if (AiJson.DecimalOrNull(root, "confidence") is { } given)
        {
            confidence = Math.Clamp(given, 0m, ConfidenceCeiling);
            if (confidence != given) rejected.Add($"confidence: {given} → {confidence}");
        }

        var dayRiskLevel = NormalizeDayRiskLevel(AiJson.StringOrNull(root, "dayRiskLevel"), rejected);
        var narrative = NormalizeNarrative(AiJson.StringOrNull(root, "narrative"), rejected);

        // Bước 2, 3, 4 — cửa sổ chặn.
        var windows = ValidateWindows(root, providedCalendar, utcNow, settings, rejected);

        return new DailyBriefResult
        {
            Accepted = true,
            DayRiskLevel = dayRiskLevel,
            Narrative = narrative,
            Confidence = confidence,
            ExtraBlackouts = windows,
            RejectedFields = rejected,
        };
    }

    private static string? NormalizeDayRiskLevel(string? value, List<string> rejected)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var match = DayRiskLevels.FirstOrDefault(
            level => string.Equals(level, value.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is null) rejected.Add($"dayRiskLevel: '{value}' không thuộc bảng");
        return match;
    }

    private static string? NormalizeNarrative(string? value, List<string> rejected)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (trimmed.Length <= NarrativeMaxLength) return trimmed;

        rejected.Add($"narrative: cắt từ {trimmed.Length} về {NarrativeMaxLength} ký tự");
        return trimmed[..NarrativeMaxLength];
    }

    private static IReadOnlyList<AiBlackoutProposal> ValidateWindows(
        JsonElement root,
        IReadOnlyList<ScheduledEvent> providedCalendar,
        DateTime utcNow,
        EngineSetting settings,
        List<string> rejected)
    {
        if (!root.TryGetProperty("extraBlackouts", out var array) || array.ValueKind != JsonValueKind.Array)
            return Array.Empty<AiBlackoutProposal>();

        var maxMinutes = Math.Max(0, settings.AiBlackoutMaxMinutes);
        var horizon = utcNow.Add(ProposalHorizon);
        var accepted = new List<AiBlackoutProposal>();
        var index = -1;

        foreach (var item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object) continue;

            if (!TryDate(AiJson.StringOrNull(item, "fromUtc"), out var from) ||
                !TryDate(AiJson.StringOrNull(item, "toUtc"), out var to))
            {
                rejected.Add($"extraBlackouts[{index}]: mốc thời gian không đọc được");
                continue;
            }

            // Bước 4 — mốc bắt đầu phải nằm trong tầm nhìn. Neo phép kiểm tra vào `fromUtc`
            // vì `toUtc` còn bị cắt ở bước 3 ngay dưới đây.
            if (to <= from || from < utcNow || from > horizon)
            {
                rejected.Add($"extraBlackouts[{index}]: khoảng thời gian không hợp lệ");
                continue;
            }

            // Bước 2 — trùng sự kiện đã có trong lịch. AI KHÔNG được sinh lại thứ đã nạp tay:
            // lịch nạp tay có giờ chính xác từ BLS/Fed, còn AI thì đang nhớ lại.
            var duplicate = providedCalendar
                .FirstOrDefault(e => e.OccursAtUtc >= from && e.OccursAtUtc <= to);

            if (duplicate is not null)
            {
                rejected.Add($"extraBlackouts[{index}]: trùng sự kiện đã có trong lịch ('{duplicate.Title}')");
                continue;
            }

            // Bước 3 — cắt độ dài về trần cấu hình.
            var capped = to;
            if ((to - from).TotalMinutes > maxMinutes)
            {
                capped = from.AddMinutes(maxMinutes);
                rejected.Add($"extraBlackouts[{index}].toUtc: cắt về {maxMinutes} phút");
            }

            var severity = ContextSeverity.CapAt(
                AiJson.StringOrNull(item, "severity"), ContextSeverity.High);

            if (ContextSeverity.Rank(severity) < ContextSeverity.Rank(ContextSeverity.Medium))
                severity = ContextSeverity.Medium;

            accepted.Add(new AiBlackoutProposal(
                from, capped,
                AiJson.StringOrNull(item, "reason")?.Trim() ?? "Tin đột xuất do AI phát hiện",
                severity));
        }

        return accepted;
    }

    private static bool TryDate(string? value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;

        utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }
}
