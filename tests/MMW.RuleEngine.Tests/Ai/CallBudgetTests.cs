using MMW.Application.Ai;
using MMW.Application.Services;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// SC-005 (dưới 30 lần gọi AI mỗi ngày) và FR-049 (vòng chấm điểm và vòng quản lý vị thế
/// gọi ĐÚNG 0 lần).
/// </summary>
/// <remarks>
/// Đây là điều kiện chấp nhận đo bằng SỐ LẦN GỌI, không đo bằng kết quả — nên nó không suy ra
/// được từ bất kỳ test hành vi nào. Một lời gọi AI lọt vào vòng chấm điểm sẽ không làm test nào
/// khác đỏ: nó chỉ làm hoá đơn tăng và làm vòng quyết định phụ thuộc vào một dịch vụ có thể chết.
/// </remarks>
public class CallBudgetTests
{
    private const string ValidBrief = """
        {"dayRiskLevel":"normal","narrative":"Ngày bình thường","confidence":0.5,
         "extraBlackouts":[],"themes":[],"symbolNotes":[]}
        """;

    private const string ValidNews = """
        {"severity":"low","affectedSymbols":["BTCUSDT"],"leaning":"neutral",
         "halfLifeMinutes":120,"isRumor":false}
        """;

    [Fact]
    public async Task Vong_cham_diem_goi_AI_dung_0_lan()
    {
        using var h = await AiHarness.CreateAsync();
        await h.AddPlanAsync();
        h.Llm.DefaultResponse = ValidNews;

        // Bốn nhịp mỗi giờ × 24 giờ: đúng số lần job `signal-eval` chạy trong một ngày.
        for (var i = 0; i < 96; i++)
        {
            using var scope = h.NewScope();
            await h.Resolve<ISignalEvalService>(scope).EvaluateAllAsync(h.AccountId, h.Clock.UtcNow);
            h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(15);
        }

        Assert.Equal(0, h.Llm.CallCount);
    }

    [Fact]
    public async Task Vong_quan_ly_vi_the_goi_AI_dung_0_lan()
    {
        using var h = await AiHarness.CreateAsync();
        await h.AddPlanAsync();
        h.Llm.DefaultResponse = ValidNews;

        // Job `position-manage` chạy mỗi phút; lấy một giờ là đủ để bắt lời gọi lọt lưới.
        for (var i = 0; i < 60; i++)
        {
            using var scope = h.NewScope();
            await h.Resolve<IPositionManageService>(scope).RunAsync(h.AccountId, h.Clock.UtcNow);
            h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        }

        Assert.Equal(0, h.Llm.CallCount);
    }

    [Fact]
    public async Task Mot_ngay_giao_dich_day_du_ton_duoi_30_lan_goi()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.DefaultResponse = ValidBrief;
        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        h.Llm.DefaultResponse = ValidNews;

        // Nguồn tin bơm ba tiêu đề MỚI mỗi nhịp 15 phút suốt cả ngày — kịch bản tệ nhất
        // về chi phí, không phải kịch bản trung bình.
        for (var run = 0; run < 96; run++)
        {
            for (var k = 0; k < 3; k++)
                h.Headlines.Add($"tin-{run}-{k}", $"Tiêu đề {run}-{k}");

            using var scope = h.NewScope();
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();
            h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(15);
        }

        Assert.True(h.Llm.CallCount < 30,
            $"Một ngày giao dịch tốn {h.Llm.CallCount} lần gọi AI, vượt trần 30 của SC-005.");
    }

    [Fact]
    public async Task Tin_da_phan_loai_roi_thi_khong_goi_lai()
    {
        using var h = await AiHarness.CreateAsync();
        h.Llm.DefaultResponse = ValidNews;
        h.Headlines.Add("tin-1", "Tiêu đề duy nhất");

        for (var i = 0; i < 5; i++)
        {
            using var scope = h.NewScope();
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();
        }

        Assert.Equal(1, h.Llm.CallCount);
    }

    [Fact]
    public async Task Tran_so_lan_goi_moi_ngay_doc_tu_cau_hinh_chu_khong_phai_hang_so()
    {
        // Nguyên tắc I: ngân sách là khẩu vị, không phải thuật toán. Hạ trần trong cấu hình
        // phải có tác dụng ngay, nếu không thì con số trong cấu hình chỉ để trang trí.
        using var h = await AiHarness.CreateAsync(s =>
        {
            s.AiMaxNewsCallsPerDay = 4;
            s.AiMaxNewsCallsPerRun = 2;
        });

        h.Llm.DefaultResponse = ValidNews;

        for (var run = 0; run < 20; run++)
        {
            for (var k = 0; k < 3; k++)
                h.Headlines.Add($"tin-{run}-{k}", $"Tiêu đề {run}-{k}");

            using var scope = h.NewScope();
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();
        }

        Assert.Equal(4, h.Llm.CallCount);
    }

    [Fact]
    public async Task Sang_ngay_moi_thi_ngan_sach_tin_duoc_dat_lai()
    {
        using var h = await AiHarness.CreateAsync(s => s.AiMaxNewsCallsPerDay = 2);
        h.Llm.DefaultResponse = ValidNews;

        for (var k = 0; k < 5; k++) h.Headlines.Add($"hom-nay-{k}", $"Tiêu đề {k}");
        using (var scope = h.NewScope())
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();

        Assert.Equal(2, h.Llm.CallCount);

        h.Clock.UtcNow = h.Clock.UtcNow.Date.AddDays(1).AddHours(1);
        using (var scope = h.NewScope())
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();

        Assert.Equal(4, h.Llm.CallCount);
    }

    [Fact]
    public async Task Nguon_tin_loi_thi_khong_goi_AI_va_khong_nem()
    {
        using var h = await AiHarness.CreateAsync();
        h.Llm.DefaultResponse = ValidNews;
        h.Headlines.Throws = true;

        using var scope = h.NewScope();
        var written = await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();

        Assert.Equal(0, written);
        Assert.Equal(0, h.Llm.CallCount);
    }
}
