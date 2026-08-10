using Microsoft.EntityFrameworkCore;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Trading.TimeGuard;

/// <summary>Điểm chất lượng khung giờ, 0–6 (FR-030, FR-031).</summary>
/// <param name="Score">0–6.</param>
/// <param name="Label">Tên khung phiên, lấy từ bảng cấu hình.</param>
/// <param name="IsPersonalised">Đúng khi điểm này thực sự tính từ lệnh đã đóng của trader.</param>
/// <param name="SampleSize">Số lệnh thắng+thua của chính khung phiên này. Hoà vốn không tính.</param>
public sealed record SessionQuality(int Score, string Label, bool IsPersonalised, int SampleSize);

public interface ISessionQualityProvider
{
    Task<SessionQuality> GetAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

/// <summary>
/// Dùng bảng phiên chuẩn khi tài khoản chưa đủ
/// <see cref="EngineSetting.PersonalStatsMinClosedTrades"/> lệnh đã đóng, sau đó chuyển sang
/// tỷ lệ thắng thật theo khung phiên.
/// </summary>
/// <remarks>
/// Điểm cá nhân được KÉO VỀ bảng chuẩn theo cỡ mẫu thay vì chia thẳng. Đủ 50 lệnh trải trên
/// 6 khung phiên nghĩa là mỗi khung chỉ khoảng 8 lệnh; chia thẳng sẽ cho một khung có đúng
/// một lệnh thua điểm 0 và cấm cửa nó vĩnh viễn dựa trên một mẫu duy nhất.
///
/// Thống kê gom theo KHUNG PHIÊN chứ không theo từng giờ: chia 50 lệnh cho 24 giờ thì giờ nào
/// cũng chỉ có một hai mẫu, và điểm sẽ nhảy loạn theo nhiễu chứ không theo kỹ năng.
/// </remarks>
public sealed class SessionQualityProvider : ISessionQualityProvider
{
    /// <summary>Thang điểm phiên. Trùng với ràng buộc 0–6 của <see cref="SessionQualityRow"/>.</summary>
    private const int MaxScore = 6;

    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<Trade> _trades;

    public SessionQualityProvider(IBaseRepository<EngineSetting> settings, IBaseRepository<Trade> trades)
    {
        _settings = settings;
        _trades = trades;
    }

    public async Task<SessionQuality> GetAsync(
        long tradingAccountId, DateTime utcNow, CancellationToken ct = default)
    {
        var setting = await _settings
            .Get(s => s.TradingAccountId == tradingAccountId)
            .Include(s => s.SessionQualityRows)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Tài khoản {tradingAccountId} chưa có cấu hình engine (EngineSetting).");

        var hour = utcNow.Hour;
        var row = setting.SessionQualityRows
            .FirstOrDefault(r => hour >= r.FromHourUtc && hour < r.ToHourUtc)
            ?? throw new InvalidOperationException(
                $"Bảng chất lượng phiên của tài khoản {tradingAccountId} không phủ giờ {hour} UTC. " +
                "Cấu hình thủng lỗ phải nổ ra ở đây, chứ trả 0 điểm thì mỗi ngày đúng khung giờ đó " +
                "lại mất điểm mà không ai biết vì sao.");

        var closed = await _trades
            .Get(t => t.TradingAccountId == tradingAccountId
                      && t.Status == TradeStatus.Closed
                      && t.OpenedAt != null)
            .AsNoTracking()
            .Select(t => new { t.OpenedAt, t.Outcome })
            .ToListAsync(ct);

        var inRow = closed
            .Where(t => t.OpenedAt!.Value.Hour >= row.FromHourUtc && t.OpenedAt.Value.Hour < row.ToHourUtc)
            .ToList();

        // Hoà vốn không phải thắng cũng không phải thua — để nó vào mẫu số sẽ kéo tỷ lệ thắng
        // xuống một cách vô cớ.
        var wins = inRow.Count(t => t.Outcome == TradeOutcome.Win);
        var losses = inRow.Count(t => t.Outcome == TradeOutcome.Loss);
        var sample = wins + losses;

        if (closed.Count < setting.PersonalStatsMinClosedTrades || sample == 0)
            return new SessionQuality(row.Score, row.Label, IsPersonalised: false, sample);

        var smoothing = Math.Max(1, setting.SessionStatsSmoothingTrades);
        var winRate = (double)wins / sample;

        // Trung bình có trọng số giữa số thật (cỡ mẫu `sample`) và điểm chuẩn (cỡ mẫu ảo `smoothing`).
        var blended = (sample * winRate * MaxScore + smoothing * row.Score) / (double)(sample + smoothing);
        var score = Math.Clamp((int)Math.Round(blended, MidpointRounding.AwayFromZero), 0, MaxScore);

        return new SessionQuality(score, row.Label, IsPersonalised: true, sample);
    }
}
