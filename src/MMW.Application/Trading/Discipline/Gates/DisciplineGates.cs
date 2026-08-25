using MMW.Domain.Constants;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Discipline.Gates;

// ─────────────────────────────────────────────────────────────────────────
// discipline.loss_streak
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Chuỗi thua liên tiếp: giảm kích thước ở ngưỡng thứ nhất, dừng ngày ở ngưỡng thứ hai.
/// </summary>
/// <remarks>
/// Hai ngưỡng tách rời có chủ ý. Thua hai lệnh liên tiếp là chuyện thống kê bình thường ngay
/// cả với một hệ thống tốt — phản ứng đúng là nhỏ lại, không phải dừng. Thua ba lệnh liên tiếp
/// thì xác suất "hôm nay đọc sai thị trường" đã đủ cao để cái giá của việc dừng rẻ hơn cái giá
/// của việc tiếp tục.
/// </remarks>
public sealed class LossStreakGate : IDisciplineGate
{
    public string Key => "discipline.loss_streak";

    public GateResult Evaluate(DisciplineContext context)
    {
        var streak = context.Stats.ConsecutiveLosses;
        var streakToday = context.Stats.ConsecutiveLossesToday;
        var stopAt = context.RiskSettings.LossStreakThreshold;
        var halveAt = context.Settings.LossStreakSizeHalveAt;

        if (streakToday >= stopAt)
        {
            return GateResult.StopDay(VetoReason.LossStreakStop,
                $"Hôm nay đã thua {streakToday} lệnh liên tiếp (ngưỡng dừng ngày {stopAt}) — dừng giao dịch đến hết ngày UTC.");
        }

        if (streak >= halveAt)
        {
            var multiplier = context.Settings.LossStreakSizeMultiplier;
            return GateResult.Reduce(multiplier,
                $"Đã thua {streak} lệnh liên tiếp (ngưỡng giảm kích thước {halveAt}) — nhân kích thước {multiplier:N2}.");
        }

        return GateResult.Pass($"Chuỗi thua hiện tại {streak}, dưới ngưỡng giảm kích thước {halveAt}.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.daily_loss_limit
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Chạm giới hạn lỗ trong ngày thì dừng, không thương lượng.</summary>
public sealed class DailyLossLimitGate : IDisciplineGate
{
    public string Key => "discipline.daily_loss_limit";

    public GateResult Evaluate(DisciplineContext context)
    {
        var lossPercent = context.Stats.DailyLossPercent;
        var limit = context.RiskSettings.MaxDailyLossPercent;

        if (lossPercent >= limit)
        {
            return GateResult.StopDay(VetoReason.DailyLossStop,
                $"Lỗ trong ngày {lossPercent:N2}% đã chạm giới hạn {limit:N2}% — dừng giao dịch đến hết ngày UTC.");
        }

        return GateResult.Pass($"Lỗ trong ngày {lossPercent:N2}%, dưới giới hạn {limit:N2}%.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.revenge_window
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Cửa sổ sau một lệnh thua. Vào lệnh trong khoảng này thường là phản ứng cảm xúc, không phải
/// một setup mới.
/// </summary>
/// <remarks>
/// Đọc <see cref="Domain.Entities.EngineSetting.RevengeBlockMinutes"/> chứ KHÔNG đọc
/// <c>RiskSetting.RevengeTradeWindowMinutes</c>. Hai con số tách rời có chủ ý: cái kia là ngưỡng
/// CẢNH BÁO của nhật ký hành vi (30 phút), cái này là ngưỡng CHẶN (15 phút). Gộp chung sẽ buộc
/// phải chọn một trong hai vai trò và làm hỏng vai còn lại.
/// </remarks>
public sealed class RevengeWindowGate : IDisciplineGate
{
    public string Key => "discipline.revenge_window";

    public GateResult Evaluate(DisciplineContext context)
    {
        var lastLoss = context.Stats.LastLossClosedAtUtc;
        var window = context.Settings.RevengeBlockMinutes;

        if (lastLoss is null)
            return GateResult.Pass("Chưa có lệnh thua nào để tính cửa sổ chặn.");

        var elapsed = context.EvaluatedAtUtc - lastLoss.Value;

        if (elapsed.TotalMinutes < window)
        {
            return GateResult.Block(VetoReason.RevengeWindow,
                $"Mới {elapsed.TotalMinutes:N0} phút kể từ lệnh thua gần nhất, chưa đủ {window} phút chờ.");
        }

        return GateResult.Pass($"Đã {elapsed.TotalMinutes:N0} phút kể từ lệnh thua gần nhất (cần {window}).");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.oversized
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Kích thước lệnh vượt xa mức thường ngày.
/// </summary>
/// <remarks>
/// So sánh dùng dấu LỚN HƠN CHẶT: đúng bằng bội số ngưỡng thì cho qua. Một lệnh to đúng bằng
/// giới hạn là lệnh nằm trong kế hoạch; chặn nó biến giới hạn thành "không được chạm tới",
/// và người dùng sẽ mất niềm tin vào con số họ tự đặt.
/// </remarks>
public sealed class OversizedGate : IDisciplineGate
{
    public string Key => "discipline.oversized";

    public GateResult Evaluate(DisciplineContext context)
    {
        var average = context.Stats.AverageRiskRecent;
        if (average is null or <= 0m)
            return GateResult.Pass("Chưa đủ lịch sử để tính kích thước trung bình.");

        var limit = average.Value * context.Settings.OversizeBlockMultiple;
        var planned = context.PlannedRiskPercent;

        if (planned > limit)
        {
            var multiplier = planned <= 0m ? 1m : limit / planned;
            return GateResult.Reduce(multiplier,
                $"Rủi ro dự kiến {planned:N2}% vượt {context.Settings.OversizeBlockMultiple:N2} lần " +
                $"trung bình {context.Settings.OversizeLookbackTrades} lệnh gần nhất ({average:N2}%, trần {limit:N2}%) — " +
                $"tự co size còn {multiplier:N2}, không veto cứng.");
        }

        return GateResult.Pass($"Rủi ro dự kiến {planned:N2}%, trong trần {limit:N2}%.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.max_trades
// ─────────────────────────────────────────────────────────────────────────

/// <summary>Đã đủ số lệnh kế hoạch ngày cho phép.</summary>
public sealed class MaxTradesGate : IDisciplineGate
{
    public string Key => "discipline.max_trades";

    public GateResult Evaluate(DisciplineContext context)
    {
        var today = context.Stats.TradesToday;
        var max = context.DailyPlan.MaxTradesToday;

        if (today >= max)
        {
            return GateResult.Block(VetoReason.MaxTradesReached,
                $"Đã vào {today} lệnh hôm nay, đủ hạn mức {max} của kế hoạch ngày.");
        }

        return GateResult.Pass($"Đã vào {today}/{max} lệnh hôm nay.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.open_position
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Đã có vị thế mở trên cùng mã, hoặc đã chạm trần số vị thế đồng thời.
/// </summary>
/// <remarks>
/// Gate này lấp một lỗ hổng mà <see cref="MaxTradesGate"/> KHÔNG che được, và khác biệt giữa hai
/// cái là điểm quan trọng nhất ở đây: <c>MaxTradesGate</c> đếm số lệnh đã VÀO trong ngày, còn
/// gate này đếm số lệnh đang CHẠY.
///
/// Vì sao thiếu nó lại nguy hiểm: điều kiện tạo ra một phiếu ≥55 điểm không biến mất sau một
/// nến. BOS đã retest thành công vẫn thành công ở nến sau, chồng EMA vẫn xếp đúng, regime ngày
/// không đổi. Một setup tốt vì vậy chấm đạt liên tục 3–5 nến liền và được vào 3–5 lần. Đó không
/// phải ba lệnh độc lập mà là MỘT ý tưởng vào làm ba lần, cùng chiều, cùng mã, dừng lỗ nằm sát
/// nhau — tương quan xấp xỉ 1,0. Với <c>SizeMultiplierMax</c> 1.5 và hạn mức 5 lệnh/ngày, trần
/// lý thuyết là 7,5R rủi ro trên MỘT ý tưởng.
///
/// Tác hại thứ hai âm thầm hơn: khi các lệnh không độc lập, <c>WinRate</c> và <c>ExpectancyR</c>
/// tính trên mỗi lệnh không còn là cơ sở hợp lệ để quyết định kích thước. Ba lệnh chồng nhau
/// cùng thắng được đếm là ba thắng, nhưng đó là một sự kiện — mẫu trông lớn hơn thực tế.
/// </remarks>
public sealed class OpenPositionGate : IDisciplineGate
{
    public string Key => "discipline.open_position";

    public GateResult Evaluate(DisciplineContext context)
    {
        var open = context.Stats.OpenPositions;

        var sameSymbol = open.FirstOrDefault(p =>
            string.Equals(p.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase));

        if (sameSymbol is not null)
        {
            return GateResult.Block(VetoReason.PositionAlreadyOpen,
                $"Đang có vị thế {Describe(sameSymbol.Direction)} mở trên {context.Symbol} " +
                $"({sameSymbol.SizeR:N2}R) — setup mới trên cùng mã là cùng một ý tưởng vào lần hai, " +
                "không phải một lệnh độc lập.");
        }

        // Cùng tài sản gốc, khác tài sản định giá. BTCUSDT và BTCUSDC là hai hợp đồng riêng trên
        // sàn nhưng bám giá nhau trong khoảng vài phần vạn — chúng là MỘT phơi nhiễm.
        //
        // Rào này sinh ra cùng lúc với việc chạy hai bộ luật song song, nơi đường swing được đẩy
        // sang các cặp USDC đúng để tránh gặp đường trong ngày. Cách tách đó giải quyết được
        // chuyện ký quỹ và chuyện khoá chống trùng, nhưng KHÔNG giải quyết được chuyện phơi
        // nhiễm — và nếu không chặn ở đây thì:
        //   • cùng chiều  → một lệnh cỡ đôi đội lốt hai lệnh trên hai mã
        //   • ngược chiều → tự hedge chính mình, trả phí và funding hai chân để giữ một vị thế
        //     ròng gần bằng không
        // Cả hai đều vô hình với rào tương quan, vì nó cộng dồn theo chiều chứ không theo mã.
        var sameAsset = open.FirstOrDefault(p =>
            SymbolConventions.SameBaseAsset(p.Symbol, context.Symbol)
            && !string.Equals(p.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase));

        if (sameAsset is not null)
        {
            var opposed = sameAsset.Direction != context.Direction;
            return GateResult.Block(VetoReason.PositionAlreadyOpen,
                $"Đang có vị thế {Describe(sameAsset.Direction)} mở trên {sameAsset.Symbol} " +
                $"({sameAsset.SizeR:N2}R) — cùng tài sản gốc {SymbolConventions.BaseAssetOf(context.Symbol)} " +
                $"với {context.Symbol}, chỉ khác tài sản định giá. " +
                (opposed
                    ? "Vào ngược chiều là tự hedge chính mình: phơi nhiễm ròng gần bằng không mà vẫn trả phí hai chân."
                    : "Vào cùng chiều là một lệnh cỡ đôi đội lốt hai lệnh trên hai mã."));
        }

        // Hạn mức đếm RIÊNG từng nhóm. Một lệnh swing 4h giữ nhiều ngày; nếu nó chiếm chỗ trong
        // cùng hạn mức với lệnh trong ngày thì hai lệnh swing là đủ khoá đường vào của bộ luật
        // ngắn hạn suốt cả tuần — và điều đó xảy ra âm thầm, không có lý do nào hiện ra ngoài
        // một dòng "đủ trần vị thế" chẳng nhắc gì tới việc nó bị nhóm khác chiếm.
        var style = context.Settings.StrategyVersion.StyleOf();
        var sameStyle = open.Where(p => p.Style == style).ToList();

        var limit = style == TradeStyle.HtfSwing
            ? context.Settings.V7MaxConcurrentSwingPositions
            : context.Settings.MaxConcurrentPositions;

        var styleName = style == TradeStyle.HtfSwing ? "lệnh H4" : "lệnh ngắn";

        if (sameStyle.Count >= limit)
        {
            return GateResult.Block(VetoReason.ConcurrentPositionLimit,
                $"Đang mở {sameStyle.Count} vị thế {styleName} ({string.Join(", ", sameStyle.Select(p => p.Symbol))}), " +
                $"đủ trần {limit} vị thế đồng thời của nhóm này.");
        }

        return GateResult.Pass(
            $"Đang mở {sameStyle.Count}/{limit} vị thế {styleName}, không có vị thế nào trên {context.Symbol}.");
    }

    private static string Describe(TradeDirection d) => d == TradeDirection.Long ? "mua" : "bán";
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.correlated_exposure
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tổng rủi ro trên các vị thế CÙNG CHIỀU và đi cùng pha với thị trường.
/// </summary>
/// <remarks>
/// BTCUSDT và ETHUSDT tương quan thường xuyên trên 0,85. Hai lệnh mua full size trên hai mã đó
/// không phải hai lệnh — nó là một lệnh 2R đội lốt phân bổ rủi ro.
///
/// Trớ trêu là <c>market.leader_correlation</c> THƯỞNG 4 điểm cho tương quan cao. Điều đó đúng ở
/// tầng một lệnh (mã đang đi cùng bối cảnh chung, dễ đọc hơn) nhưng sai ở tầng danh mục, nơi
/// tương quan cao nghĩa là không có phân tán nào cả. Hai tầng cần hai câu trả lời khác nhau cho
/// cùng một con số, nên chúng phải nằm ở hai chỗ khác nhau.
///
/// <b>Đây là phép XẤP XỈ và cố ý dừng ở mức xấp xỉ.</b> Tương quan từng cặp giữa mọi mã đang mở
/// đòi hỏi một ma trận phải dựng lại mỗi lần chấm. Ở đây dùng tương quan với mã dẫn dắt làm đại
/// diện: hai mã cùng bám sát BTC thì cũng bám sát nhau. Sai số của phép xấp xỉ nghiêng về phía
/// thận trọng, vì nó không bao giờ kết luận "hai mã này độc lập" khi cả hai đều đang bám BTC.
///
/// Giảm size chứ KHÔNG chặn: rủi ro tương quan là chuyện của liều lượng, không phải chuyện đúng
/// sai. Setup thứ hai vẫn có thể là một setup tốt.
/// </remarks>
public sealed class CorrelatedExposureGate : IDisciplineGate
{
    public string Key => "discipline.correlated_exposure";

    public GateResult Evaluate(DisciplineContext context)
    {
        var strong = context.Settings.LeaderCorrelationStrong;

        // Mã dẫn dắt luôn "đi cùng pha với chính nó". Mã khác cần đo được và đo đủ mạnh.
        var movesWithMarket = context.IsLeaderSymbol
                              || (context.LeaderCorrelation is { } c && Math.Abs(c) >= strong);

        if (!movesWithMarket)
        {
            return GateResult.Pass(context.LeaderCorrelation is { } value
                ? $"Tương quan với mã dẫn dắt {value:N2}, dưới mức đồng pha {strong:N2} — không cộng dồn rủi ro."
                : $"Không đo được tương quan với mã dẫn dắt (ngưỡng đồng pha {strong:N2}, " +
                  $"đang mở {context.Stats.OpenPositions.Count} vị thế) — không cộng dồn rủi ro.");
        }

        var sameSide = context.Stats.OpenPositions
            .Where(p => p.Direction == context.Direction)
            .Sum(p => p.SizeR);

        var limit = context.Settings.MaxCorrelatedR;
        var projected = sameSide + context.ProjectedSizeR;

        if (projected <= limit)
        {
            return GateResult.Pass(
                $"Rủi ro cùng chiều sau lệnh này {projected:N2}R, trong trần tương quan {limit:N2}R.");
        }

        var room = limit - sameSide;
        if (room <= 0m)
        {
            return GateResult.Reduce(0m,
                $"Đã dùng hết {sameSide:N2}R rủi ro cùng chiều trên nhóm mã đồng pha (trần {limit:N2}R) — " +
                "không còn chỗ cho lệnh này.");
        }

        var multiplier = room / context.ProjectedSizeR;
        return GateResult.Reduce(multiplier,
            $"Đang có {sameSide:N2}R cùng chiều trên nhóm mã đồng pha; lệnh này {context.ProjectedSizeR:N2}R " +
            $"sẽ vượt trần {limit:N2}R — co còn {multiplier:N2} để tổng dừng đúng ở trần.");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// discipline.worst_hours
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Giờ hiện tại nằm trong hai khung giờ trader thua nhiều nhất.
/// </summary>
/// <remarks>
/// Gate DUY NHẤT phụ thuộc số lệnh lịch sử. Dưới ngưỡng mẫu nó trả <c>Allow</c> với phạt 0 —
/// <b>không</b> trả điểm thưởng. Khác biệt này quan trọng: "chưa đủ dữ liệu để biết giờ này
/// xấu" không giống "giờ này tốt", và cho điểm thưởng ở đó sẽ khiến tài khoản mới vào lệnh
/// tự tin hơn tài khoản đã có lịch sử.
/// </remarks>
public sealed class WorstHoursGate : IDisciplineGate
{
    private const decimal WorstHourSizeMultiplier = 0.5m;

    public string Key => "discipline.worst_hours";

    public GateResult Evaluate(DisciplineContext context)
    {
        var required = context.Settings.PersonalStatsMinClosedTrades;
        var closed = context.Stats.ClosedTradeCount;

        if (closed < required)
            return GateResult.Pass($"Mới {closed}/{required} lệnh đã đóng, chưa đủ để xếp hạng khung giờ.");

        var hour = context.EvaluatedAtUtc.Hour;
        if (!context.Stats.WorstHoursUtc.Contains(hour))
            return GateResult.Pass($"Giờ {hour:00} UTC không nằm trong nhóm giờ thua nhiều nhất.");

        var penalty = context.Settings.WorstHoursPenalty;
        return GateResult.ReduceAndPenalise(WorstHourSizeMultiplier, -penalty,
            $"Giờ {hour:00} UTC nằm trong nhóm giờ thua nhiều nhất của bạn ({closed} lệnh đã đóng) — " +
            $"trừ {penalty} điểm và giảm size còn {WorstHourSizeMultiplier:N2}.");
    }
}
