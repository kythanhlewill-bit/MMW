using MMW.Application.Abstractions;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Ai;

public interface IMarketContextApplier
{
    /// <summary>
    /// Hệ số AI áp cho kích thước lệnh. Kết quả LUÔN nằm trong <c>[0.0, 1.0]</c>.
    /// Không có bối cảnh, bối cảnh hết hạn, hoặc AI chết → trả về <c>1.0</c>.
    /// </summary>
    decimal GetSizeMultiplier(
        IReadOnlyList<MarketContextRecord> activeContext, string symbol, TradeDirection direction);
}

/// <summary>
/// Điểm cưỡng chế duy nhất của ranh giới "AI chỉ được nói không".
/// </summary>
/// <remarks>
/// Kiểu trả về là một số trong <c>[0, 1]</c> nhân vào kích thước. Không tồn tại đường nào cho
/// phép AI trả về giá trị lớn hơn 1 — AI không làm lệnh to lên được vì KHÔNG CÓ PHÉP TOÁN NÀO
/// cho phép điều đó, chứ không phải vì có một câu <c>if</c> chặn lại.
///
/// Lớp này thuần và tất định: nó không chạm mạng, không chạm cơ sở dữ liệu, và không biết
/// <c>ILlmService</c> tồn tại. Nhờ vậy nó vẫn nằm ngoài tầng quyết định (bộ gác
/// <c>NoAiInTradingTests</c>) mà tầng quyết định vẫn gọi được nó.
/// </remarks>
public sealed class MarketContextApplier : IMarketContextApplier
{
    private readonly IClock _clock;

    public MarketContextApplier(IClock clock) => _clock = clock;

    public decimal GetSizeMultiplier(
        IReadOnlyList<MarketContextRecord> activeContext, string symbol, TradeDirection direction)
    {
        if (activeContext is null || activeContext.Count == 0) return 1.0m;

        var utcNow = _clock.UtcNow;
        var multiplier = 1.0m;

        foreach (var record in activeContext)
        {
            // Lọc hạn LẦN HAI. Người gọi đã lọc khi truy vấn, nhưng một bản ghi hết hạn lọt
            // qua đây sẽ veto một lệnh hợp lệ bằng một tin của hôm qua, và không để lại dấu
            // vết nào ngoài một phiếu chấm điểm khó hiểu.
            if (record.ExpiresAtUtc <= utcNow) continue;
            if (!record.AppliesTo(symbol)) continue;
            if (!Opposes(record.Leaning, direction)) continue;

            // Lấy mức chặt nhất, KHÔNG nhân dồn: nhân dồn khiến ảnh hưởng của AI tỉ lệ với độ
            // ồn của nguồn tin, và một luồng tin lắm lời sẽ lặng lẽ bóp chết việc vào lệnh.
            multiplier = Math.Min(multiplier, ContextSeverity.SizeMultiplier(record.Severity));
        }

        return Math.Clamp(multiplier, 0m, 1m);
    }

    /// <summary>
    /// Bối cảnh có ngược chiều lệnh không. Bối cảnh THUẬN chiều không làm tăng hệ số.
    /// </summary>
    /// <remarks>
    /// <c>Neutral</c> tính là ngược cho CẢ HAI chiều, và đây là một quyết định có cân nhắc.
    /// Hợp đồng viết "chỉ áp khi ngược chiều lệnh"; đọc chặt chữ thì một tin
    /// "sàn lớn bị hack, chưa rõ giá chạy hướng nào" — tin nguy hiểm nhất trong ngày — sẽ
    /// không áp cho lệnh nào cả. Diễn giải ở đây chỉ đi về phía THẬN TRỌNG HƠN, nên nó không
    /// phá ràng buộc gốc: AI vẫn chỉ có một hướng tác động, và hướng đó là xuống.
    /// </remarks>
    private static bool Opposes(MarketBias leaning, TradeDirection direction) => leaning switch
    {
        MarketBias.Bearish => direction == TradeDirection.Long,
        MarketBias.Bullish => direction == TradeDirection.Short,
        _ => true,
    };
}
