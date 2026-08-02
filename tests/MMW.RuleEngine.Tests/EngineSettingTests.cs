using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// <see cref="EngineSetting"/> là nơi mọi ngưỡng của thuật toán sống, nên một cấu hình sai
/// không làm hệ thống lỗi — nó làm hệ thống chạy SAI trong im lặng. Bảng phiên thủng một giờ
/// sẽ biến thành "thiếu dữ liệu ⟹ 0 điểm" đúng vào giờ đó, mỗi ngày, không ai biết.
/// Vì vậy ràng buộc phải được kiểm tra khi LƯU chứ không phải khi đọc.
/// </summary>
public class EngineSettingTests
{
    private static List<SessionQualityRow> DefaultSessions() => new()
    {
        new() { FromHourUtc = 0,  ToHourUtc = 7,  Score = 2, Label = "Phiên Á" },
        new() { FromHourUtc = 7,  ToHourUtc = 9,  Score = 5, Label = "Mở cửa London" },
        new() { FromHourUtc = 9,  ToHourUtc = 13, Score = 5, Label = "London" },
        new() { FromHourUtc = 13, ToHourUtc = 16, Score = 6, Label = "Chồng lấn New York" },
        new() { FromHourUtc = 16, ToHourUtc = 21, Score = 4, Label = "New York chiều" },
        new() { FromHourUtc = 21, ToHourUtc = 24, Score = 1, Label = "Đêm mỏng" },
    };

    private static EngineSetting Valid()
    {
        var s = new EngineSetting();
        foreach (var row in DefaultSessions()) s.SessionQualityRows.Add(row);
        return s;
    }

    [Fact]
    public void Cau_hinh_mac_dinh_theo_dac_ta_la_hop_le()
    {
        Assert.Empty(Valid().Validate());
    }

    // ── Ngưỡng điểm ─────────────────────────────────────────────────────

    [Fact]
    public void Nguong_diem_phai_khong_giam_dan()
    {
        var s = Valid();
        s.MinScoreToEnter = 80;
        s.ScoreThresholdFull = 70;

        Assert.Contains(s.Validate(), e => e.Contains("MinScoreToEnter"));
    }

    [Fact]
    public void Nguong_vao_lenh_toi_da_khong_duoc_thap_hon_nguong_day_du()
    {
        var s = Valid();
        s.ScoreThresholdFull = 90;
        s.ScoreThresholdMax = 85;

        Assert.Contains(s.Validate(), e => e.Contains("ScoreThresholdFull"));
    }

    [Fact]
    public void He_so_kich_thuoc_phai_khong_giam_dan()
    {
        var s = Valid();
        s.SizeMultiplierLow = 1.2m;
        s.SizeMultiplierFull = 1.0m;

        Assert.Contains(s.Validate(), e => e.Contains("SizeMultiplier"));
    }

    // ── Trọng số nhóm ───────────────────────────────────────────────────

    [Fact]
    public void Tong_ba_trong_so_nhom_phai_bang_85()
    {
        // 15 điểm còn lại của thang 100 thuộc nhóm kỷ luật, vốn CHỈ TRỪ.
        // Nghĩa là điểm 100 tuyệt đối không đạt được — đó là thiết kế có chủ ý:
        // không có setup nào hoàn hảo, và thang điểm không nên gợi ý điều ngược lại.
        var s = Valid();
        Assert.Equal(85, s.WeightTechnical + s.WeightMarket + s.WeightLiquidity);

        s.WeightLiquidity = 30;
        Assert.Contains(s.Validate(), e => e.Contains("85"));
    }

    // ── Bảng chất lượng phiên ───────────────────────────────────────────

    [Fact]
    public void Bang_phien_phai_phu_kin_0_den_24()
    {
        var s = Valid();
        s.SessionQualityRows.Clear();
        foreach (var r in DefaultSessions().Where(r => r.FromHourUtc != 21)) s.SessionQualityRows.Add(r);

        Assert.Contains(s.Validate(), e => e.Contains("phủ kín"));
    }

    [Fact]
    public void Bang_phien_khong_duoc_thung_lo_o_giua()
    {
        var s = Valid();
        s.SessionQualityRows.Clear();
        foreach (var r in DefaultSessions())
        {
            if (r.FromHourUtc == 9) continue;   // bỏ khoảng 9–13 → hở đúng giữa bảng
            s.SessionQualityRows.Add(r);
        }

        Assert.Contains(s.Validate(), e => e.Contains("phủ kín") || e.Contains("liền mạch"));
    }

    [Fact]
    public void Bang_phien_khong_duoc_chong_lan()
    {
        var s = Valid();
        s.SessionQualityRows.Clear();
        foreach (var r in DefaultSessions()) s.SessionQualityRows.Add(r);
        s.SessionQualityRows.First(r => r.FromHourUtc == 7).ToHourUtc = 11;   // đè lên khoảng 9–13

        Assert.Contains(s.Validate(), e => e.Contains("chồng lấn") || e.Contains("liền mạch"));
    }

    [Fact]
    public void Diem_phien_phai_nam_trong_0_den_6()
    {
        var s = Valid();
        s.SessionQualityRows.First().Score = 9;

        Assert.Contains(s.Validate(), e => e.Contains("0–6"));
    }

    [Fact]
    public void Bang_phien_rong_la_khong_hop_le()
    {
        var s = Valid();
        s.SessionQualityRows.Clear();

        Assert.NotEmpty(s.Validate());
    }

    // ── Ngưỡng chặn kỷ luật ─────────────────────────────────────────────

    [Fact]
    public void Nguong_chan_revenge_tach_rieng_khoi_nguong_canh_bao()
    {
        // RiskSetting.RevengeTradeWindowMinutes = 30 là ngưỡng CẢNH BÁO.
        // EngineSetting.RevengeBlockMinutes = 15 là ngưỡng CHẶN.
        // Gộp chung sẽ buộc phải chọn một vai và làm hỏng vai còn lại.
        Assert.Equal(15, new EngineSetting().RevengeBlockMinutes);
        Assert.Equal(30, new RiskSetting().RevengeTradeWindowMinutes);
    }

    [Fact]
    public void Cac_gia_tri_mac_dinh_khop_dac_ta()
    {
        var s = new EngineSetting();

        Assert.Equal(55, s.MinScoreToEnter);
        Assert.Equal(70, s.ScoreThresholdFull);
        Assert.Equal(85, s.ScoreThresholdMax);
        Assert.Equal(0.5m, s.SizeMultiplierLow);
        Assert.Equal(1.0m, s.SizeMultiplierFull);
        Assert.Equal(1.5m, s.SizeMultiplierMax);
        Assert.Equal(50, s.PersonalStatsMinClosedTrades);
        Assert.Equal(120, s.AiBlackoutMaxMinutes);
        Assert.Equal("15m", s.EntryTimeframe);
        Assert.Equal("4h", s.BiasTimeframe);
    }

    [Fact]
    public void Bang_luat_chan_khong_duoc_trung_loai_su_kien()
    {
        var s = Valid();
        s.BlackoutRules.Add(new BlackoutRule { EventKind = ScheduledEventKind.Cpi, MinutesBefore = 60, MinutesAfter = 30 });
        s.BlackoutRules.Add(new BlackoutRule { EventKind = ScheduledEventKind.Cpi, MinutesBefore = 10, MinutesAfter = 5 });

        Assert.Contains(s.Validate(), e => e.Contains("trùng"));
    }

    [Fact]
    public void Cua_so_chan_khong_duoc_am()
    {
        var s = Valid();
        s.BlackoutRules.Add(new BlackoutRule { EventKind = ScheduledEventKind.Cpi, MinutesBefore = -5, MinutesAfter = 30 });

        Assert.Contains(s.Validate(), e => e.Contains("âm"));
    }
}
