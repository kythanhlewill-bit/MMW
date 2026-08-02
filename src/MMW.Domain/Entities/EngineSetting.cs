using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>
/// Mọi ngưỡng của Deterministic Intraday Trading Engine (1:1 với TradingAccount).
/// </summary>
/// <remarks>
/// Thực thể này tồn tại để thoả Nguyên tắc I của hiến chương: không hằng số nào của thuật toán
/// được viết thẳng vào lớp tính toán. Các giá trị mặc định dưới đây là giá trị seed lấy từ
/// đặc tả — chúng là ĐIỂM XUẤT PHÁT cấu hình được, không phải hằng số của mã.
/// </remarks>
public class EngineSetting : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    // ── Ngưỡng điểm (FR-033) ────────────────────────────────────────────
    /// <summary>Dưới ngưỡng này thì không vào lệnh.</summary>
    public int MinScoreToEnter { get; set; } = 55;
    /// <summary>Từ ngưỡng này vào kích thước đầy đủ.</summary>
    public int ScoreThresholdFull { get; set; } = 70;
    /// <summary>Từ ngưỡng này vào kích thước tối đa.</summary>
    public int ScoreThresholdMax { get; set; } = 85;

    [Precision(9, 4)] public decimal SizeMultiplierLow { get; set; } = 0.5m;
    [Precision(9, 4)] public decimal SizeMultiplierFull { get; set; } = 1.0m;
    [Precision(9, 4)] public decimal SizeMultiplierMax { get; set; } = 1.5m;

    // ── Trọng số nhóm (FR-025) ──────────────────────────────────────────
    public int WeightTechnical { get; set; } = 40;
    public int WeightMarket { get; set; } = 30;
    public int WeightLiquidity { get; set; } = 15;

    // ── Tham số kỹ thuật (R-007, R-008) ─────────────────────────────────
    /// <summary>Số nến hai bên để xác nhận một điểm xoay fractal.</summary>
    public int SwingPivotBars { get; set; } = 2;
    /// <summary>Số nến tối đa chờ giá kiểm định lại vùng đã phá vỡ.</summary>
    public int RetestWindowBars { get; set; } = 6;
    /// <summary>Giá đã chạy quá số ATR này khỏi vùng xác nhận thì "vị trí vào lệnh" = 0 điểm.</summary>
    [Precision(9, 4)] public decimal MaxAtrFromConfirmation { get; set; } = 1.5m;

    public string EntryTimeframe { get; set; } = "15m";
    public string BiasTimeframe { get; set; } = "4h";

    // ── Chuyển sang thống kê cá nhân (FR-030) ───────────────────────────
    /// <summary>Dưới số lệnh đã đóng này thì dùng bảng phiên chuẩn, không dùng thống kê giờ cá nhân.</summary>
    public int PersonalStatsMinClosedTrades { get; set; } = 50;
    public int WorstHoursPenalty { get; set; } = 10;

    // ── Kỷ luật (FR-035) ────────────────────────────────────────────────
    public int LossStreakSizeHalveAt { get; set; } = 2;

    /// <summary>
    /// Tách khỏi <see cref="RiskSetting.RevengeTradeWindowMinutes"/> (30) có chủ ý:
    /// đây là ngưỡng CHẶN lệnh, còn kia là ngưỡng CẢNH BÁO. Gộp chung sẽ buộc phải
    /// chọn một trong hai vai trò và làm hỏng vai còn lại.
    /// </summary>
    public int RevengeBlockMinutes { get; set; } = 15;

    [Precision(9, 4)] public decimal OversizeBlockMultiple { get; set; } = 1.5m;
    public int OversizeLookbackTrades { get; set; } = 20;

    // ── Lớp AI (FR-011, FR-044) ─────────────────────────────────────────
    /// <summary>Trần độ dài một cửa sổ chặn do AI đề xuất. Dài hơn thì cắt về đây.</summary>
    public int AiBlackoutMaxMinutes { get; set; } = 120;
    public int AiContextDefaultTtlMinutes { get; set; } = 240;

    // ── Kiểm thử lịch sử (R-012) ────────────────────────────────────────
    [Precision(9, 4)] public decimal BacktestTakerFeePercent { get; set; } = 0.05m;
    [Precision(9, 4)] public decimal BacktestEntrySlippageBps { get; set; } = 1m;
    [Precision(9, 4)] public decimal BacktestStopSlippageBps { get; set; } = 3m;

    // ── Chế độ so sánh song song (FR-059) ───────────────────────────────
    public bool ShadowAiComparisonEnabled { get; set; } = true;

    public ICollection<SessionQualityRow> SessionQualityRows { get; set; } = new List<SessionQualityRow>();
    public ICollection<BlackoutRule> BlackoutRules { get; set; } = new List<BlackoutRule>();

    /// <summary>
    /// Kiểm tra toàn bộ ràng buộc cấu hình. Rỗng nghĩa là hợp lệ.
    /// </summary>
    /// <remarks>
    /// PHẢI gọi khi LƯU, không phải khi đọc. Một cấu hình sai không làm hệ thống lỗi —
    /// nó làm hệ thống chạy sai trong im lặng. Bảng phiên thủng một giờ sẽ biến thành
    /// "thiếu dữ liệu ⟹ 0 điểm" đúng vào giờ đó, mỗi ngày, và không ai biết.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // ── Ngưỡng điểm phải không giảm dần ─────────────────────────────
        if (MinScoreToEnter > ScoreThresholdFull)
            errors.Add($"MinScoreToEnter ({MinScoreToEnter}) không được lớn hơn ScoreThresholdFull ({ScoreThresholdFull}).");

        if (ScoreThresholdFull > ScoreThresholdMax)
            errors.Add($"ScoreThresholdFull ({ScoreThresholdFull}) không được lớn hơn ScoreThresholdMax ({ScoreThresholdMax}).");

        if (SizeMultiplierLow > SizeMultiplierFull || SizeMultiplierFull > SizeMultiplierMax)
            errors.Add($"SizeMultiplier phải không giảm dần: {SizeMultiplierLow} / {SizeMultiplierFull} / {SizeMultiplierMax}.");

        // ── Trọng số nhóm ───────────────────────────────────────────────
        var weightSum = WeightTechnical + WeightMarket + WeightLiquidity;
        if (weightSum != 85)
            errors.Add($"Tổng ba trọng số nhóm phải bằng 85 (hiện {weightSum}); 15 điểm còn lại thuộc nhóm kỷ luật chỉ-trừ.");

        errors.AddRange(ValidateSessionTable());
        errors.AddRange(ValidateBlackoutRules());
        return errors;
    }

    private IEnumerable<string> ValidateSessionTable()
    {
        var rows = SessionQualityRows.OrderBy(r => r.FromHourUtc).ToList();

        if (rows.Count == 0)
        {
            yield return "Bảng chất lượng phiên rỗng: phải phủ kín 0–24.";
            yield break;
        }

        foreach (var r in rows.Where(r => r.Score is < 0 or > 6))
            yield return $"Điểm phiên của khoảng {r.FromHourUtc}–{r.ToHourUtc} là {r.Score}, phải nằm trong 0–6.";

        foreach (var r in rows.Where(r => r.FromHourUtc >= r.ToHourUtc))
            yield return $"Khoảng phiên {r.FromHourUtc}–{r.ToHourUtc} không hợp lệ: giờ bắt đầu phải nhỏ hơn giờ kết thúc.";

        // Đi tuần tự từ 0: mỗi khoảng phải nối đúng vào điểm kết thúc của khoảng trước.
        // Cách này bắt được cả lỗ hổng lẫn chồng lấn bằng cùng một phép kiểm tra.
        var cursor = 0;
        foreach (var r in rows)
        {
            if (r.FromHourUtc != cursor)
            {
                yield return r.FromHourUtc > cursor
                    ? $"Bảng phiên không phủ kín: hở từ giờ {cursor} đến {r.FromHourUtc}."
                    : $"Bảng phiên chồng lấn tại giờ {r.FromHourUtc} (khoảng trước đã kết thúc ở {cursor}).";
                yield break;
            }
            cursor = r.ToHourUtc;
        }

        if (cursor != 24)
            yield return $"Bảng phiên không phủ kín: kết thúc ở giờ {cursor}, phải là 24.";
    }

    private IEnumerable<string> ValidateBlackoutRules()
    {
        foreach (var g in BlackoutRules.GroupBy(r => r.EventKind).Where(g => g.Count() > 1))
            yield return $"Luật chặn trùng loại sự kiện {g.Key}: có {g.Count()} dòng, chỉ được 1.";

        foreach (var r in BlackoutRules.Where(r => r.MinutesBefore < 0 || r.MinutesAfter < 0))
            yield return $"Cửa sổ chặn của {r.EventKind} có giá trị âm ({r.MinutesBefore}/{r.MinutesAfter}).";
    }
}
