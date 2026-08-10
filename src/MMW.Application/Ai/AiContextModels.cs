using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Ai;

/// <summary>
/// Thang mức nghiêm trọng của bối cảnh và quyền hạn tương ứng của lớp AI.
/// </summary>
/// <remarks>
/// Bảng hệ số dưới đây nằm trong MÃ chứ không trong <c>EngineSetting</c>, và đó là chủ ý.
/// Nó không phải một ngưỡng khẩu vị như "điểm bao nhiêu thì vào lệnh" — nó là ĐỊNH NGHĨA
/// trần quyền hạn của lớp AI, cùng loại với chu kỳ EMA 20/50/200. Đưa nó vào cấu hình nghĩa
/// là mở một ô nhập cho phép nới quyền của AI bằng một lần sửa dữ liệu, đúng thứ mà toàn bộ
/// US6 tồn tại để ngăn.
///
/// Mọi giá trị đều nằm trong <c>[0, 1]</c>. Không có mức nào cho hệ số lớn hơn 1 vì không
/// có mức nào ĐƯỢC PHÉP làm lệnh to lên (FR-042).
/// </remarks>
public static class ContextSeverity
{
    public const string Noise = "noise";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";

    private static readonly string[] Ladder = [Noise, Low, Medium, High, Critical];

    /// <summary>Bậc trên thang, 0 = <c>noise</c>. Chuỗi lạ trả 0 — không rõ thì coi như nhiễu.</summary>
    public static int Rank(string? severity)
    {
        var index = Array.FindIndex(Ladder, s =>
            string.Equals(s, severity?.Trim(), StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : index;
    }

    /// <summary>Đưa về đúng một trong năm giá trị hợp lệ. Không nhận ra thì về <c>noise</c>.</summary>
    public static string Normalize(string? severity) => Ladder[Rank(severity)];

    /// <summary>Hạ mức nghiêm trọng xuống trần cho trước; không bao giờ nâng lên.</summary>
    public static string CapAt(string? severity, string ceiling) =>
        Ladder[Math.Min(Rank(severity), Rank(ceiling))];

    /// <summary>Hệ số kích thước theo hợp đồng. Luôn nằm trong <c>[0, 1]</c>.</summary>
    public static decimal SizeMultiplier(string? severity) => Rank(severity) switch
    {
        0 => 1.0m,   // noise
        1 => 1.0m,   // low
        2 => 0.75m,  // medium
        3 => 0.5m,   // high
        _ => 0.0m,   // critical — veto hoàn toàn
    };
}

/// <summary>Một cửa sổ chặn do AI đề xuất, đã qua kiểm chứng phía nhận.</summary>
public sealed record AiBlackoutProposal(DateTime FromUtc, DateTime ToUtc, string Reason, string Severity);

/// <summary>Kết quả Daily Brief SAU khi đã soi đủ sáu bước kiểm chứng.</summary>
/// <remarks>
/// <see cref="Accepted"/> bằng <c>false</c> nghĩa là bối cảnh trung tính: vòng quyết định chạy
/// y như khi lớp AI không tồn tại. Đó là trạng thái mặc định an toàn, không phải trạng thái lỗi.
/// </remarks>
public sealed record DailyBriefResult
{
    public bool Accepted { get; init; }
    public string? DayRiskLevel { get; init; }
    public string? Narrative { get; init; }
    public decimal? Confidence { get; init; }
    public IReadOnlyList<AiBlackoutProposal> ExtraBlackouts { get; init; } = Array.Empty<AiBlackoutProposal>();

    /// <summary>Các trường bị cắt hoặc bị loại. Khác rỗng là tín hiệu lời nhắc đang trôi.</summary>
    public IReadOnlyList<string> RejectedFields { get; init; } = Array.Empty<string>();

    public static DailyBriefResult Neutral(params string[] rejected) =>
        new() { Accepted = false, RejectedFields = rejected };
}

/// <summary>Kết quả phân loại một tiêu đề tin, SAU khi đã soi bốn bước kiểm chứng.</summary>
public sealed record NewsClassification
{
    public bool Accepted { get; init; }
    public string Severity { get; init; } = ContextSeverity.Noise;
    public MarketBias Leaning { get; init; } = MarketBias.Neutral;
    public IReadOnlyList<string> AffectedSymbols { get; init; } = Array.Empty<string>();
    public int HalfLifeMinutes { get; init; }
    public bool IsRumor { get; init; }
    public IReadOnlyList<string> RejectedFields { get; init; } = Array.Empty<string>();

    public static NewsClassification Neutral(params string[] rejected) =>
        new() { Accepted = false, Severity = ContextSeverity.Noise, RejectedFields = rejected };
}

public static class MarketContextRecordExtensions
{
    /// <summary>
    /// Bản ghi này có nói về mã đang xét không.
    /// </summary>
    /// <remarks>
    /// Danh sách mã rỗng nghĩa là TOÀN THỊ TRƯỜNG, không phải "không áp cho ai". Một tin vĩ mô
    /// hiếm khi nêu tên mã, và đọc rỗng thành vô hại sẽ vô hiệu hoá đúng nhóm tin nguy hiểm nhất.
    /// </remarks>
    public static bool AppliesTo(this MarketContextRecord record, string symbol)
    {
        if (string.IsNullOrWhiteSpace(record.AffectedSymbols)) return true;

        return record.AffectedSymbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase));
    }
}
