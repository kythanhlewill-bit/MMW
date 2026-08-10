using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// T067 / FR-020 — nhiều dòng của bảng FR-019 cùng khớp thì lấy <c>MIN</c> hệ số, <c>MIN</c>
/// số lệnh, và <b>giao</b> của các chiều được phép.
/// </summary>
/// <remarks>
/// Đây là quy tắc cần nhất quán tuyệt đối, vì nó chạy vào đúng những ngày nguy hiểm nhất:
/// ngày vừa có tin, vừa biến động cực đoan, vừa đang trong xu hướng. Lấy dòng khớp đầu tiên
/// thay vì lấy nhỏ nhất là lỗi im lặng — nó cho ra một con số hợp lệ, chỉ là con số sai, và
/// sai về phía mạo hiểm hơn.
/// </remarks>
public class RegimeMergeTests
{
    [Fact]
    public void Vi_du_kiem_chung_cua_contract_tang_cong_cuc_doan_cong_ngay_tin()
    {
        // min(1.0, 1.0, 0.3, 0.4) = 0.3 · min(5, 5, 2, 2) = 2 · LongOnly ∩ Both ∩ Both = LongOnly
        var r = RegimeTable.Resolve(DayStructure.TrendUp, VolatilityRegime.Extreme, hasHighImpactEvent: true);

        Assert.Equal(0.3m, r.RiskMultiplier);
        Assert.Equal(2, r.MaxTradesToday);
        Assert.Equal(AllowedDirections.LongOnly, r.AllowedDirections);
    }

    [Fact]
    public void Lay_he_so_NHO_NHAT_chu_khong_phai_dong_khop_dau_tien()
    {
        // Dòng "ngày có tin" (0.4) đứng cuối bảng nhưng phải thắng dòng "xu hướng tăng
        // + bình thường" (1.0) đứng đầu.
        var r = RegimeTable.Resolve(DayStructure.TrendUp, VolatilityRegime.Normal, hasHighImpactEvent: true);

        Assert.Equal(0.4m, r.RiskMultiplier);
        Assert.Equal(2, r.MaxTradesToday);
    }

    [Fact]
    public void Cuc_doan_thang_ngay_tin_vi_no_nho_hon()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Extreme, hasHighImpactEvent: true);

        Assert.Equal(0.3m, r.RiskMultiplier);
        Assert.Equal(2, r.MaxTradesToday);
    }

    [Fact]
    public void Giao_chieu_chu_khong_phai_hop_chieu()
    {
        // Ngày giảm + cực đoan: dòng cực đoan cho "cả hai" nhưng dòng nền cho "chỉ bán".
        // Hợp lại sẽ ra "cả hai" và mở cửa cho lệnh mua ngược xu hướng — đúng thứ bị cấm.
        var r = RegimeTable.Resolve(DayStructure.TrendDown, VolatilityRegime.Extreme, hasHighImpactEvent: true);

        Assert.Equal(AllowedDirections.ShortOnly, r.AllowedDirections);
    }

    [Theory]
    [InlineData(AllowedDirections.Both, AllowedDirections.Both, AllowedDirections.Both)]
    [InlineData(AllowedDirections.Both, AllowedDirections.LongOnly, AllowedDirections.LongOnly)]
    [InlineData(AllowedDirections.Both, AllowedDirections.ShortOnly, AllowedDirections.ShortOnly)]
    [InlineData(AllowedDirections.LongOnly, AllowedDirections.LongOnly, AllowedDirections.LongOnly)]
    [InlineData(AllowedDirections.LongOnly, AllowedDirections.ShortOnly, AllowedDirections.None)]
    [InlineData(AllowedDirections.None, AllowedDirections.Both, AllowedDirections.None)]
    public void Phep_giao_chieu_dung_tren_moi_to_hop(
        AllowedDirections a, AllowedDirections b, AllowedDirections expected)
    {
        Assert.Equal(expected, RegimeTable.Intersect(a, b));
    }

    [Fact]
    public void Phep_giao_chieu_doi_xung()
    {
        var values = Enum.GetValues<AllowedDirections>();

        foreach (var a in values)
        foreach (var b in values)
        {
            Assert.Equal(RegimeTable.Intersect(a, b), RegimeTable.Intersect(b, a));
        }
    }

    [Fact]
    public void Them_mot_dieu_kien_khop_khong_bao_gio_lam_ket_qua_thoang_hon()
    {
        // Tính chất bao trùm cả bảng: bật thêm bất kỳ điều kiện nào cũng chỉ được siết lại.
        // Kiểm bằng cách quét toàn bộ tổ hợp thay vì tin vào vài ca mẫu.
        foreach (var structure in Enum.GetValues<DayStructure>())
        foreach (var vol in Enum.GetValues<VolatilityRegime>())
        {
            var without = RegimeTable.Resolve(structure, vol, hasHighImpactEvent: false);
            var with = RegimeTable.Resolve(structure, vol, hasHighImpactEvent: true);

            Assert.True(with.RiskMultiplier <= without.RiskMultiplier,
                $"{structure}/{vol}: thêm ngày tin làm hệ số tăng từ {without.RiskMultiplier} lên {with.RiskMultiplier}.");
            Assert.True(with.MaxTradesToday <= without.MaxTradesToday,
                $"{structure}/{vol}: thêm ngày tin làm số lệnh tăng từ {without.MaxTradesToday} lên {with.MaxTradesToday}.");
        }
    }

    [Fact]
    public void Bien_dong_cang_cao_khong_bao_gio_duoc_noi_long_hon()
    {
        foreach (var structure in Enum.GetValues<DayStructure>())
        {
            var normal = RegimeTable.Resolve(structure, VolatilityRegime.Normal, false);
            var extreme = RegimeTable.Resolve(structure, VolatilityRegime.Extreme, false);

            Assert.True(extreme.RiskMultiplier <= normal.RiskMultiplier);
            Assert.True(extreme.MaxTradesToday <= normal.MaxTradesToday);
        }
    }

    [Fact]
    public void Moi_to_hop_deu_cho_ket_qua_hop_le()
    {
        foreach (var structure in Enum.GetValues<DayStructure>())
        foreach (var vol in Enum.GetValues<VolatilityRegime>())
        foreach (var hasEvent in new[] { false, true })
        {
            var r = RegimeTable.Resolve(structure, vol, hasEvent);

            Assert.InRange(r.RiskMultiplier, 0m, 1.0m);
            Assert.InRange(r.MaxTradesToday, 0, 5);
        }
    }
}
