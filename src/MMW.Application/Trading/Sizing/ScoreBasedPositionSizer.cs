using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Sizing;

/// <param name="BaseSizeR">Kích thước theo bảng ngưỡng điểm, trước mọi hệ số.</param>
/// <param name="DataMultiplier">
/// Tỉ lệ điểm ĐO ĐƯỢC trên tổng thang điểm. Cùng một setup, càng thiếu dữ liệu thì vào càng nhỏ.
/// </param>
/// <param name="FinalSizeR">Tích của kích thước gốc với bốn hệ số. Luôn ≤ <paramref name="BaseSizeR"/>.</param>
public sealed record SizingResult(
    decimal BaseSizeR,
    decimal DayRiskMultiplier,
    decimal DisciplineMultiplier,
    decimal AiMultiplier,
    decimal FinalSizeR,
    string ReasonVi,
    decimal DataMultiplier = 1m,
    decimal SetupMultiplier = 1m);

public sealed record SetupSizingProfile(SetupType SetupType, int QualityScore);

public interface IPositionSizer
{
    SizingResult Calculate(
        ScoringOutcome score,
        DailyPlan plan,
        GateAggregate gates,
        decimal aiMultiplier,
        EngineSetting settings,
        SetupSizingProfile? setup = null,
        TradeDirection? direction = null);
}

/// <summary>
/// Kích thước lệnh theo điểm số (FR-034).
/// </summary>
/// <remarks>
/// Cả bốn hệ số đều được KẸP về <c>[0, 1]</c> trước khi nhân. Đó là lý do bất biến
/// "<c>finalSizeR ≤ baseSizeR</c>" là một tính chất SỐ HỌC chứ không phải một quy ước phải nhớ:
/// tích của một số không âm với bốn số trong <c>[0, 1]</c> không thể lớn hơn chính nó.
///
/// Đặc biệt với hệ số AI. FR-042 nói AI chỉ được veto hoặc giảm; nếu lớp AI trả về 1.5 vì lỗi
/// bóc tách hay vì mô hình "tự tin", phép kẹp biến nó thành 1.0 và không có gì xảy ra. Cưỡng
/// chế ở phía NHẬN, không phải bằng lời dặn trong prompt.
/// </remarks>
public sealed class ScoreBasedPositionSizer : IPositionSizer
{
    public SizingResult Calculate(
        ScoringOutcome score,
        DailyPlan plan,
        GateAggregate gates,
        decimal aiMultiplier,
        EngineSetting settings,
        SetupSizingProfile? setup = null,
        TradeDirection? direction = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(settings);

        var day = Clamp01(plan.RiskMultiplier);
        var gate = Clamp01(gates.SizeMultiplier);
        var ai = Clamp01(aiMultiplier);
        var data = Clamp01(score.DataCoverage);

        if (score.IsVetoed)
            return Zero(day, gate, ai, data, $"Bị veto cứng: {score.VetoReason}.");

        if (gates.IsBlocked)
            return Zero(day, gate, ai, data, $"Bị gate kỷ luật chặn: {gates.VetoReason}.");

        // Ngưỡng so theo TỈ LỆ trên phần điểm đo được, không so tuyệt đối. Xem chú thích của
        // ScoringOutcome: so tuyệt đối làm kiểm thử lịch sử lọc gắt hơn chạy thật gần 9 điểm
        // phần trăm, và lệch đó chỉ có một chiều — làm báo cáo đẹp hơn thực tế.
        // Setup bán khống phải trả thêm ShortEntryScorePenalty điểm. Áp ở ĐÂY chứ không ở bộ
        // kích hoạt là có chủ ý: với các bộ luật trigger-first, SignalEvalService nâng điểm lên
        // đúng MinScoreToEnter khi trigger xác nhận, nên một cổng đặt trước đó sẽ bị chính phép
        // nâng ấy vô hiệu hoá. Đặt sau, khoản thuế đòi điểm THẬT vượt sàn chứ không nhận điểm
        // được nâng — đúng ý định: chiều đang lỗ phải chứng minh nhiều hơn, không phải chỉ có
        // một bộ kích hoạt hợp lệ. Xem EngineSetting.ShortEntryScorePenalty cho số đo.
        var shortPenalty = direction == TradeDirection.Short ? Math.Max(0, settings.ShortEntryScorePenalty) : 0;
        var entryThreshold = settings.MinScoreToEnter + shortPenalty;

        if (!score.Reaches(entryThreshold))
        {
            var scale = score.AvailableMaxPoints == score.TotalMaxPoints
                ? string.Empty
                : $" (đo được {score.AvailableMaxPoints}/{score.TotalMaxPoints} điểm ⟹ cần {Required(score, entryThreshold)})";
            var penaltyNote = shortPenalty > 0
                ? $" (đã gồm {shortPenalty} điểm phụ thu chiều bán khống)"
                : string.Empty;

            return Zero(day, gate, ai, data,
                $"Điểm {score.TotalScore} dưới ngưỡng vào lệnh {entryThreshold}{penaltyNote}{scale} — không vào lệnh. " +
                "Zero lệnh là kết quả đúng, không phải lỗi.");
        }

        var baseSize = BaseSize(score, settings);
        var setupMultiplier = 1m;

        // V7 dùng chung đường này với V6, và đó không phải tiện tay: với cả hai, thứ quyết định
        // cỡ lệnh là CHẤT LƯỢNG SETUP chứ không phải điểm bối cảnh. Ở V7 điều đó còn bắt buộc
        // hơn — bộ kích hoạt ghi đè điểm về đúng ngưỡng vào lệnh khi setup xác nhận, nên nếu chỉ
        // đọc điểm thì mọi lệnh swing đều vào bằng cùng một cỡ nhỏ nhất, và cả thang hợp lưu
        // 2–4 lớp trở thành vô nghĩa.
        //
        // Ba mốc chất lượng dùng lại của V6 (60/70/85). Chúng là mốc trên thang 0–100 chung, và
        // điểm chất lượng của V7 cũng được dựng trong khoảng 60–100 để nằm đúng thang đó.
        var qualitySized = settings.StrategyVersion.UsesSidewaysV6()
                           || settings.StrategyVersion.UsesHtfSwing();

        if (setup is not null && qualitySized)
        {
            baseSize = Math.Min(baseSize, RiskCap(setup.SetupType, settings));
            setupMultiplier = QualityMultiplier(setup.QualityScore, settings);
        }

        // Bốn hệ số điều chỉnh NHÂN nhau nên chúng teo theo cấp số nhân. Kẹp TÍCH của chúng về
        // sàn cấu hình để biên độ ngân sách rủi ro giữa các lệnh không mở ra cả chục lần — xem
        // EngineSetting.MinSizeMultiplierProduct cho số đo và cái giá đã trả.
        //
        // Chỉ kẹp khi tích đang DƯƠNG. Một hệ số bằng 0 là một câu trả lời "không", không phải
        // một sự thận trọng quá đà: kế hoạch ngày cấm rủi ro, gate kỷ luật chặn, AI veto, hay
        // không có dữ liệu nào đo được — kẹp nó lên sẽ biến bốn cái veto thành lệnh nửa cỡ.
        var rawProduct = day * gate * ai * data;
        var product = rawProduct > 0m
            ? Math.Max(rawProduct, Math.Min(settings.MinSizeMultiplierProduct, 1m))
            : rawProduct;

        var final = baseSize * setupMultiplier * product;
        var clampNote = product > rawProduct
            ? $" (tích {rawProduct:N3} bị kẹp lên sàn {product:N2})"
            : string.Empty;

        return new SizingResult(baseSize, day, gate, ai, final,
            $"Điểm {score.TotalScore}/{score.AvailableMaxPoints} ⟹ cap setup {baseSize:N2}R, " +
            $"quality {setupMultiplier:N2} × ngày {day:N2} × " +
            $"kỷ luật {gate:N2} × AI {ai:N2} × dữ liệu {data:N2} = {final:N4}R{clampNote}.",
            data,
            setupMultiplier);
    }

    /// <summary>Bảng ngưỡng ba bậc, toàn bộ đọc từ cấu hình theo tài khoản (Nguyên tắc I).</summary>
    private static decimal BaseSize(ScoringOutcome score, EngineSetting settings)
    {
        if (score.Reaches(settings.ScoreThresholdMax)) return settings.SizeMultiplierMax;
        if (score.Reaches(settings.ScoreThresholdFull)) return settings.SizeMultiplierFull;
        return settings.SizeMultiplierLow;
    }

    /// <summary>Điểm thực tế cần đạt trên thang đã teo lại, chỉ để ghi vào lý do.</summary>
    private static int Required(ScoringOutcome score, int threshold) =>
        score.TotalMaxPoints <= 0
            ? threshold
            : (int)Math.Ceiling((decimal)threshold * score.AvailableMaxPoints / score.TotalMaxPoints);

    private static SizingResult Zero(decimal day, decimal gate, decimal ai, decimal data, string reason) =>
        new(0m, day, gate, ai, 0m, reason, data);

    private static decimal RiskCap(SetupType setup, EngineSetting settings) => setup switch
    {
        SetupType.RectangleRangeFade => settings.V6RangeRiskCap,
        SetupType.RectangleBreakout or SetupType.TriangleBreakout => settings.V6CompressionRiskCap,
        _ => settings.V6TrendRiskCap,
    };

    private static decimal QualityMultiplier(int quality, EngineSetting settings)
    {
        if (quality < settings.V6MinSetupQuality) return 0m;
        if (quality >= settings.V6SetupQualityMax) return settings.V6QualityMaxMultiplier;
        if (quality >= settings.V6SetupQualityFull) return settings.V6QualityFullMultiplier;
        return settings.V6QualityLowMultiplier;
    }

    /// <summary>
    /// Kẹp về <c>[0, 1]</c>. Hệ số âm sẽ đảo dấu kích thước, hệ số &gt; 1 sẽ phóng to lệnh —
    /// cả hai đều là lỗi lập trình, và cả hai đều phải vô hại ở đây.
    /// </summary>
    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);
}
