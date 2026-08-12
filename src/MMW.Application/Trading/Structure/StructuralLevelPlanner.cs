using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Structure;

/// <summary>Mức dừng lỗ và chốt lời suy ra từ cấu trúc giá.</summary>
/// <param name="StopLoss">Mức phủ định setup, đã cộng đệm ra ngoài điểm xoay.</param>
/// <param name="TakeProfit">Mức cấu trúc đối diện gần nhất, đã trừ đệm vào trong.</param>
/// <param name="RiskReward">Tỉ lệ <c>|target − entry| / |entry − stop|</c>.</param>
/// <param name="StopIsStructural">
/// Sai nghĩa là không đọc được điểm xoay nào và dừng lỗ đang dùng công thức ATR dự phòng.
/// </param>
/// <param name="TargetIsStructural">Sai nghĩa là mục tiêu là bội R cấu hình, không phải một mức thật.</param>
/// <param name="StopAtrMultiple">Khoảng cách dừng lỗ thực tế, tính theo bội ATR — để ghi vào phiếu.</param>
public sealed record StructuralLevels(
    decimal StopLoss,
    decimal TakeProfit,
    decimal RiskReward,
    bool StopIsStructural,
    bool TargetIsStructural,
    decimal StopAtrMultiple,
    string ReasonVi,
    decimal? FirstTakeProfit = null,
    decimal? RunnerTakeProfit = null,
    decimal? RetestEntry = null,
    decimal? FirstTargetRiskReward = null);

public sealed record StructuralLevelRequest
{
    public required decimal Entry { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Atr { get; init; }
    public required EngineSetting Settings { get; init; }

    /// <summary>Nến khung vào lệnh. Dừng lỗ CHỈ đọc từ đây.</summary>
    public required IReadOnlyList<Candle> EntryCandles { get; init; }

    /// <summary>Nến khung thiên hướng (4h). Chỉ dùng cho mục tiêu.</summary>
    public IReadOnlyList<Candle> BiasCandles { get; init; } = Array.Empty<Candle>();

    /// <summary>Nến ngày. Chỉ dùng cho mục tiêu.</summary>
    public IReadOnlyList<Candle> DailyCandles { get; init; } = Array.Empty<Candle>();

    /// <summary>Tỉ lệ lãi/lỗ dự phòng khi không tìm được mức cấu trúc đối diện nào.</summary>
    public decimal FallbackRiskReward { get; init; } = 1.5m;
}

public interface IStructuralLevelPlanner
{
    /// <summary>
    /// Dựng mức dừng lỗ và chốt lời. Trả <c>null</c> khi cấu trúc nằm quá xa để đặt dừng lỗ.
    /// </summary>
    StructuralLevels? Plan(StructuralLevelRequest request);
}

/// <summary>
/// Neo dừng lỗ và mục tiêu vào CẤU TRÚC GIÁ thay vì vào một bội ATR cố định.
/// </summary>
/// <remarks>
/// Đây là thay đổi có đòn bẩy lớn nhất của V2, và lý do nằm ở hai chỗ khác nhau.
///
/// <para><b>Thứ nhất — chỗ đặt dừng lỗ.</b> Công thức cũ đặt dừng lỗ ở đúng
/// <c>giá ± 1,5 × ATR</c>, mù hoàn toàn với đáy/đỉnh xoay gần nhất. Nếu đáy đó nằm cách giá
/// 1,2 ATR thì dừng lỗ 1,5 ATR rơi ngay DƯỚI nơi lệnh dừng của số đông đang nằm — chỗ giá bị
/// hút tới. Điều oái oăm là hệ thống đã phát hiện được tình huống này: tiêu chí
/// <c>liquidity.zone_position</c> trả đúng 0 điểm khi có cụm thanh khoản ngay ngoài dừng lỗ.
/// Nhưng phản ứng cũ là trừ 5 điểm rồi vẫn vào lệnh với đúng cái dừng lỗ đó. Phản ứng đúng là
/// DỜI dừng lỗ — việc mà lớp này làm. Tiêu chí kia đã được gỡ khỏi thang điểm ngày 2026-08-12
/// vì lớp này đã xử lý đúng tình huống đó; xem đầu tệp <c>Criteria/LiquidityCriteria.cs</c>.</para>
///
/// <para><b>Thứ hai — chi phí tính theo R.</b> Phí giao dịch quy về R tỉ lệ NGHỊCH với độ rộng
/// dừng lỗ: <c>feeR = giá × phí% / (bội ATR × ATR)</c>. Với ATR bằng 0,18% giá và dừng lỗ
/// 1,5 ATR, phí taker một chiều đã ngốn 0,185R; một lệnh thua tốn 1,52R còn một lệnh thắng tại
/// 1R chỉ thu về 0,59R. Nới dừng lỗ theo cấu trúc (trung bình ~2,5 ATR) cắt chi phí đó gần một
/// nửa mà không cần cải thiện tín hiệu chút nào.</para>
///
/// <para>Hàm THUẦN: không I/O, không đồng hồ. Mọi ngưỡng truyền từ <see cref="EngineSetting"/>.</para>
/// </remarks>
public sealed class StructuralLevelPlanner : IStructuralLevelPlanner
{
    /// <summary>
    /// Số nến gần nhất được xét khi tìm điểm phủ định setup.
    /// </summary>
    /// <remarks>
    /// Là ĐỊNH NGHĨA của "điểm xoay còn liên quan", không phải khẩu vị rủi ro. Một đáy hình thành
    /// 200 nến trước (hơn hai ngày trên khung 15m) không còn phủ định setup hôm nay — nó chỉ kéo
    /// dừng lỗ ra xa tới mức lệnh nào cũng bị trần ATR chặn lại.
    /// </remarks>
    private const int StopLookbackBars = 40;

    /// <summary>Đệm lùi vào TRƯỚC mức cấu trúc khi đặt mục tiêu, theo bội ATR.</summary>
    /// <remarks>
    /// Chốt lời phải đứng trước hàng người đang chờ ở mức đó, không phải đứng cùng hàng. Chênh
    /// lệch 0,2 ATR là khác biệt giữa "khớp đủ" và "chạm rồi quay đầu".
    /// </remarks>
    private const decimal TargetBufferAtr = 0.20m;

    /// <summary>Sàn TP1 của Standard §7.2; thấp hơn mức này, chốt 50% tạo payoff quá nhỏ.</summary>
    private const decimal FirstTargetMinRiskReward = 1.20m;

    private readonly ISwingDetector _swings;

    public StructuralLevelPlanner(ISwingDetector swings) => _swings = swings;

    public StructuralLevels? Plan(StructuralLevelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = request.Settings;
        var entry = request.Entry;
        var atr = request.Atr;

        if (entry <= 0m || atr <= 0m) return null;

        var isLong = request.Direction == TradeDirection.Long;
        var pivotBars = Math.Max(1, settings.SwingPivotBars);

        // ── Dừng lỗ: CHỈ đọc khung vào lệnh ─────────────────────────────
        // Điểm phủ định của một setup intraday nằm trên chính khung dựng ra nó. Lấy đáy khung
        // ngày làm dừng lỗ cho một lệnh 15m là cách chắc chắn để mọi lệnh đều vượt trần ATR.
        var entryPivots = _swings.Detect(
            request.EntryCandles.TakeLast(StopLookbackBars).ToList(), pivotBars);

        var invalidation = isLong
            ? entryPivots.Where(p => !p.IsHigh && p.Price < entry).Select(p => (decimal?)p.Price).Max()
            : entryPivots.Where(p => p.IsHigh && p.Price > entry).Select(p => (decimal?)p.Price).Min();

        var buffer = atr * settings.StopStructureBufferAtr;
        var minDistance = atr * settings.StopAtrMultipleMin;
        var maxDistance = atr * settings.StopAtrMultipleMax;

        decimal stop;
        bool stopIsStructural;

        if (invalidation is { } level)
        {
            stop = isLong ? level - buffer : level + buffer;
            stopIsStructural = true;
        }
        else
        {
            stop = isLong
                ? entry - atr * settings.StopAtrMultiple
                : entry + atr * settings.StopAtrMultiple;
            stopIsStructural = false;
        }

        var distance = Math.Abs(entry - stop);

        // Sàn: cấu trúc quá gần thì nới ra, nếu không dừng lỗ dính sát giá và bị quét bởi nhiễu.
        if (distance < minDistance)
        {
            distance = minDistance;
            stop = isLong ? entry - distance : entry + distance;
        }

        // Trần: cấu trúc quá xa thì KHÔNG vào lệnh. Co size rồi vẫn vào chỉ là lặp lại cùng một
        // sai lầm với chi phí thấp hơn — nếu điểm phủ định cách 3,5 ATR thì ta không đọc được
        // cấu trúc, và một lệnh dựa trên thứ ta không đọc được là một lệnh không nên có.
        if (distance > maxDistance) return null;

        // Mức retest là pivot vừa bị vượt qua nằm giữa stop và giá hiện tại. Nó phục vụ lệnh
        // chờ ở §7; không được lấy một mốc -0,25R cứng rồi gọi đó là "theo cấu trúc".
        var retestEntry = isLong
            ? entryPivots.Where(p => p.IsHigh && p.Price < entry && p.Price > stop)
                .OrderByDescending(p => p.Index).Select(p => (decimal?)p.Price).FirstOrDefault()
            : entryPivots.Where(p => !p.IsHigh && p.Price > entry && p.Price < stop)
                .OrderByDescending(p => p.Index).Select(p => (decimal?)p.Price).FirstOrDefault();

        // ── Mục tiêu: gộp mức từ cả ba khung ────────────────────────────
        var opposing = OpposingLevels(request, pivotBars, entry, isLong);
        var targetBuffer = atr * TargetBufferAtr;

        decimal firstTarget;
        decimal target;
        bool targetIsStructural;

        var targets = opposing
            .Select(level => isLong ? level - targetBuffer : level + targetBuffer)
            .Where(level => isLong ? level > entry : level < entry)
            .Distinct()
            .OrderBy(level => Math.Abs(level - entry))
            .ToList();
        var measuredTargets = targets
            .Select(level => new { Price = level, Rr = Math.Abs(level - entry) / distance })
            .ToList();
        var nearest = measuredTargets.Select(x => (decimal?)x.Price).FirstOrDefault();
        var firstPartial = measuredTargets.FirstOrDefault(x => x.Rr >= FirstTargetMinRiskReward);
        var qualifying = measuredTargets.FirstOrDefault(x => x.Rr >= settings.MinStructuralRr);

        if (qualifying is not null)
        {
            firstTarget = firstPartial?.Price ?? qualifying.Price;
            target = qualifying.Price;
            targetIsStructural = true;
        }
        else if (nearest is { } nearestTarget)
        {
            // Có cản nhưng không có mức nào đủ xa: giữ số đo thật để structural_room veto.
            // Không được dựng một target giả xuyên qua chính cản vừa phát hiện.
            firstTarget = nearestTarget;
            target = nearestTarget;
            targetIsStructural = true;
        }
        else
        {
            // Không có cản nào trong ba khung thì target theo R là đường lui hợp lệ. Nó phải
            // tự nhất quán với rào structural_room; V1 dùng fallback 1,5R rồi rào 1,6R nên
            // mọi setup không có cản bị tạo ra chỉ để chắc chắn bị loại.
            var fallbackRr = Math.Max(settings.MinStructuralRr, request.FallbackRiskReward);
            var reward = distance * Math.Max(1m, fallbackRr);
            target = isLong ? entry + reward : entry - reward;
            firstTarget = target;
            targetIsStructural = false;
        }

        var riskReward = distance <= 0m ? 0m : Math.Abs(target - entry) / distance;
        var firstRiskReward = distance <= 0m ? 0m : Math.Abs(firstTarget - entry) / distance;
        var stopAtrMultiple = distance / atr;
        decimal? runnerTarget = target != firstTarget ? target : null;

        var reason =
            $"Dừng lỗ {stop:N2} ({(stopIsStructural ? "ngoài điểm xoay khung vào lệnh" : "dự phòng theo ATR")}, " +
            $"{stopAtrMultiple:N2} ATR), TP1 {firstTarget:N2} ({firstRiskReward:N2}R), " +
            $"mục tiêu cuối {target:N2} " +
            $"({(targetIsStructural ? "mức cấu trúc đủ xa gần nhất" : "bội R dự phòng")}), " +
            $"R:R {riskReward:N2}.";

        return new StructuralLevels(
            stop, target, riskReward, stopIsStructural, targetIsStructural, stopAtrMultiple, reason,
            FirstTakeProfit: firstTarget,
            RunnerTakeProfit: runnerTarget,
            RetestEntry: retestEntry,
            FirstTargetRiskReward: firstRiskReward);
    }

    /// <summary>
    /// Mức cấu trúc chắn đường tới mục tiêu, gộp từ khung vào lệnh, khung 4h và khung ngày.
    /// </summary>
    /// <remarks>
    /// Trước V2 chỉ khung vào lệnh được nhìn, nên kháng cự 4h và mức ngày VÔ HÌNH với engine —
    /// và vào lệnh mua ngay dưới đỉnh 4h là kịch bản thua kinh điển. Dữ liệu đã nằm sẵn trong
    /// <c>ScoringContext</c> từ đầu, chỉ là không ai đọc.
    /// </remarks>
    private List<decimal> OpposingLevels(
        StructuralLevelRequest request, int pivotBars, decimal entry, bool isLong)
    {
        var levels = new List<decimal>();

        void Collect(IReadOnlyList<Candle> candles)
        {
            if (candles.Count == 0) return;

            foreach (var p in _swings.Detect(candles, pivotBars))
            {
                if (p.IsHigh != isLong) continue;                     // long cần đỉnh, short cần đáy
                if (isLong ? p.Price > entry : p.Price < entry) levels.Add(p.Price);
            }
        }

        Collect(request.EntryCandles);
        Collect(request.BiasCandles);
        Collect(request.DailyCandles);

        return levels;
    }
}
