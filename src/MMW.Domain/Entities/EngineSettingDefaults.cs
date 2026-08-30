using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Cấu hình engine mặc định: bảng chất lượng phiên và độ rộng cửa sổ chặn theo FR-010.
/// </summary>
/// <remarks>
/// Đặt ở Domain chứ không ở seeder của tầng Web vì kiểm thử cũng cần đúng bộ giá trị này.
/// Nếu test tự dựng bảng luật riêng thì nó chứng minh cho một hệ thống không tồn tại: bảng
/// luật chạy thật có thể sai mà mọi test vẫn xanh.
///
/// Đây là ĐIỂM XUẤT PHÁT cấu hình được, không phải hằng số của thuật toán (Nguyên tắc I).
/// </remarks>
public static class EngineSettingDefaults
{
    /// <summary>Tạo cấu hình đầy đủ cho một tài khoản, kèm bảng phiên và luật chặn.</summary>
    public static EngineSetting Create(long tradingAccountId)
    {
        var setting = new EngineSetting { TradingAccountId = tradingAccountId };

        foreach (var row in SessionQualityRows()) setting.SessionQualityRows.Add(row);
        foreach (var rule in BlackoutRules()) setting.BlackoutRules.Add(rule);

        return setting;
    }

    /// <summary>Bảng chất lượng phiên cold-start, dùng khi chưa đủ số lệnh để thống kê giờ cá nhân.</summary>
    /// <remarks>
    /// Bảng cũ chấm theo GIỜ VÀNG của thị trường: London và chồng lấn New York cao nhất, đêm mỏng
    /// thấp nhất. Đó là hiểu biết chung đúng cho thanh khoản, nhưng thanh khoản không phải thứ
    /// bảng này dùng để làm. Nó nhân vào cỡ lệnh, nên thứ nó phải đo là KẾT CỤC.
    ///
    /// Đo trên 2.900 phiếu có kết cục mô phỏng (net R sau phí và trượt giá), đúng theo sáu khung
    /// giờ của chính bảng này:
    /// <code>
    /// khung                điểm cũ   net R đo được   điểm mới
    /// 21–24 Đêm mỏng          1         −0,162          5      ⟵ tốt nhất, từng bị chấm thấp nhất
    /// 07–09 Mở cửa London     5         −0,185          5
    /// 16–21 New York chiều    4         −0,210          4
    /// 00–07 Phiên Á           2         −0,236          4
    /// 13–16 Chồng lấn NY      6         −0,444          2
    /// 09–13 London            5         −0,492          1      ⟵ tệ nhất, từng được chấm 5
    /// </code>
    ///
    /// Thứ tự gần như đảo ngược, và nó nhất quán với hai nguồn khác: giờ tin vĩ mô Mỹ rơi vào
    /// 12:30–14:00 UTC, còn engine này giao dịch mean-reversion trên khung 15 phút — loại setup
    /// mà biến động do tin là kẻ thù chứ không phải cơ hội.
    ///
    /// Đây là số của MỘT tài khoản trên BTC/ETH, không phải một chân lý thị trường. Nó nằm ở đây
    /// vì đây là điểm xuất phát cho chính hệ thống ấy, và vì <c>SessionQualityProvider</c> sẽ kéo
    /// nó về số liệu cá nhân khi đủ mẫu. Có mẫu lớn hơn nói ngược lại thì sửa bảng, đừng sửa
    /// provider.
    /// </remarks>
    public static IEnumerable<SessionQualityRow> SessionQualityRows() => new[]
    {
        new SessionQualityRow { FromHourUtc = 0,  ToHourUtc = 7,  Score = 4, Label = "Phiên Á" },
        new SessionQualityRow { FromHourUtc = 7,  ToHourUtc = 9,  Score = 5, Label = "Mở cửa London" },
        new SessionQualityRow { FromHourUtc = 9,  ToHourUtc = 13, Score = 1, Label = "London" },
        new SessionQualityRow { FromHourUtc = 13, ToHourUtc = 16, Score = 2, Label = "Chồng lấn New York" },
        new SessionQualityRow { FromHourUtc = 16, ToHourUtc = 21, Score = 4, Label = "New York chiều" },
        new SessionQualityRow { FromHourUtc = 21, ToHourUtc = 24, Score = 5, Label = "Đêm mỏng" },
    };

    /// <summary>
    /// Độ rộng cửa sổ chặn theo FR-010.
    /// </summary>
    /// <remarks>
    /// FR-010 liệt kê 8 NHÓM sự kiện, nhưng khoá duy nhất của bảng là (EngineSettingId, EventKind)
    /// nên phải trải thành 12 dòng — một dòng cho mỗi loại. Các loại cùng nhóm dùng chung độ rộng.
    ///
    /// Ba loại có <c>MinutesBefore = MinutesAfter = 0</c> là có chủ ý: họp báo chính sách và
    /// khoảng trống cuối tuần được chặn theo ĐỘ DÀI của chính sự kiện, không theo biên trước/sau.
    /// </remarks>
    public static IEnumerable<BlackoutRule> BlackoutRules() => new[]
    {
        // Nhóm 1 — số liệu lạm phát và việc làm, T−60 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.Cpi, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Ppi, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Nfp, MinutesBefore = 60, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 2 — công bố quyết định chính sách, T−90 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.FomcStatement, MinutesBefore = 90, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 3 — họp báo: chặn trọn độ dài buổi họp
        new BlackoutRule { EventKind = ScheduledEventKind.FomcPressConference, MinutesBefore = 0, MinutesAfter = 0, RequiresPositionAction = true },

        // Nhóm 4 — số liệu tác động vừa, T−30 → T+15
        new BlackoutRule { EventKind = ScheduledEventKind.Pce, MinutesBefore = 30, MinutesAfter = 15, RequiresPositionAction = true },
        new BlackoutRule { EventKind = ScheduledEventKind.Gdp, MinutesBefore = 30, MinutesAfter = 15 },
        new BlackoutRule { EventKind = ScheduledEventKind.JoblessClaims, MinutesBefore = 30, MinutesAfter = 15 },

        // Nhóm 5 — đáo hạn quyền chọn, T−30 → T+30
        new BlackoutRule { EventKind = ScheduledEventKind.OptionsExpiry, MinutesBefore = 30, MinutesAfter = 30, RequiresPositionAction = true },

        // Nhóm 6 — thanh toán phí vốn, T−5 → T+5
        new BlackoutRule { EventKind = ScheduledEventKind.FundingSettlement, MinutesBefore = 5, MinutesAfter = 5 },

        // Nhóm 7 — khoảng trống cuối tuần: chặn trọn độ dài sự kiện (21:00–23:00 UTC Chủ nhật)
        new BlackoutRule { EventKind = ScheduledEventKind.WeekendGap, MinutesBefore = 0, MinutesAfter = 0 },

        // Nhóm 8 — tin đột xuất, T → T+60
        new BlackoutRule { EventKind = ScheduledEventKind.AiDetectedShock, MinutesBefore = 0, MinutesAfter = 60, RequiresPositionAction = true },
    };
}
