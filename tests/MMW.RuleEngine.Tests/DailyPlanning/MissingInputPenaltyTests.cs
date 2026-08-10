using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// T068 / FR-022 và bất biến 6 — thiếu dữ liệu làm hệ số rủi ro GIẢM, và không bao giờ làm
/// việc phân loại đổ vỡ.
/// </summary>
/// <remarks>
/// Bất biến 6 quan trọng hơn vẻ ngoài của nó. Một ngoại lệ ở đây làm job kế hoạch ngày chết,
/// để hệ thống không có kế hoạch; mà theo FR-023, không có kế hoạch nghĩa là CẢ NGÀY không
/// giao dịch được. Suy biến an toàn nhưng không mong muốn — nên tránh bằng cách không ném.
/// </remarks>
public class MissingInputPenaltyTests
{
    private static readonly IDayRegimeClassifier Classifier =
        new DayRegimeClassifier(new SwingDetector(), new IndicatorService());

    private static readonly EngineSetting Settings = DailyPlanFixtures.Settings();

    /// <summary>Chuỗi 104 phiên có cấu trúc rõ và đủ mẫu để tính phân vị — không thiếu gì.</summary>
    private static List<Candle> HealthyCandles()
    {
        // 84 phiên đệm biên độ vừa, rồi 20 phiên gấp khúc tạo xu hướng tăng — vừa đủ 104 phiên
        // để có 90 mẫu phân vị, và 20 phiên cuối đúng bằng cửa sổ đọc cấu trúc.
        const int fillerDays = 84;
        var filler = DailyPlanFixtures.FlatClose(Enumerable.Repeat(4m, fillerDays));
        var zigzag = DailyPlanFixtures.ZigZag(DailyPlanFixtures.UptrendPath);

        var shifted = zigzag
            .Select((c, i) => c with
            {
                OpenTime = DailyPlanFixtures.Day0.AddDays(fillerDays + i),
                CloseTime = DailyPlanFixtures.Day0.AddDays(fillerDays + i + 1).AddTicks(-1),
            })
            .ToList();

        return filler.Concat(shifted).ToList();
    }

    private static DailyPlanInputs Complete() => new()
    {
        BtcDailyCandles = HealthyCandles(),
        SymbolDailyCandles = HealthyCandles(),
        TodayEvents = Array.Empty<ScheduledEvent>(),
        FundingRate = 0.0001m,
        OpenInterestChange24hPercent = 1.2m,
        LongShortAccountRatio = 1.1m,
        FearGreedIndex = 55,
    };

    [Fact]
    public void Du_moi_dau_vao_thi_khong_bi_phat()
    {
        var result = Classifier.Classify(Complete(), Settings);

        Assert.Empty(result.MissingInputs);
        Assert.True(result.RiskMultiplier > 0.5m,
            "Bộ dữ liệu đầy đủ phải cho hệ số vượt trần phạt, nếu không test phạt bên dưới vô nghĩa.");
    }

    [Theory]
    [InlineData(nameof(DailyPlanInputs.FundingRate), DailyPlanInputNames.FundingRate)]
    [InlineData(nameof(DailyPlanInputs.OpenInterestChange24hPercent), DailyPlanInputNames.OpenInterestChange)]
    [InlineData(nameof(DailyPlanInputs.LongShortAccountRatio), DailyPlanInputNames.LongShortRatio)]
    [InlineData(nameof(DailyPlanInputs.FearGreedIndex), DailyPlanInputNames.FearGreed)]
    public void Thieu_bat_ky_dau_vao_nao_cung_bi_phat_ve_toi_da_0_5(string property, string expectedName)
    {
        var inputs = property switch
        {
            nameof(DailyPlanInputs.FundingRate) => Complete() with { FundingRate = null },
            nameof(DailyPlanInputs.OpenInterestChange24hPercent) => Complete() with { OpenInterestChange24hPercent = null },
            nameof(DailyPlanInputs.LongShortAccountRatio) => Complete() with { LongShortAccountRatio = null },
            _ => Complete() with { FearGreedIndex = null },
        };

        var result = Classifier.Classify(inputs, Settings);

        Assert.Contains(expectedName, result.MissingInputs);
        Assert.True(result.RiskMultiplier <= 0.5m,
            $"Thiếu {expectedName} mà hệ số vẫn là {result.RiskMultiplier}.");
    }

    [Fact]
    public void Phat_thieu_du_lieu_la_TRAN_chu_khong_phai_gia_tri_co_dinh()
    {
        // Ngày cực đoan cho 0.3; thiếu dữ liệu không được kéo NGƯỢC lên 0.5.
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(10m, 79).Concat(Enumerable.Repeat(100m, 25)));

        var result = Classifier.Classify(new DailyPlanInputs
        {
            BtcDailyCandles = candles,
            SymbolDailyCandles = candles,
            TodayEvents = Array.Empty<ScheduledEvent>(),
        }, Settings);

        Assert.NotEmpty(result.MissingInputs);
        Assert.Equal(0.3m, result.RiskMultiplier);
    }

    [Fact]
    public void Thieu_nhieu_dau_vao_thi_liet_ke_du_khong_dung_lai_o_cai_dau_tien()
    {
        var result = Classifier.Classify(Complete() with
        {
            FundingRate = null,
            FearGreedIndex = null,
        }, Settings);

        Assert.Contains(DailyPlanInputNames.FundingRate, result.MissingInputs);
        Assert.Contains(DailyPlanInputNames.FearGreed, result.MissingInputs);
    }

    // ── Bất biến 6: không bao giờ ném ───────────────────────────────────

    [Fact]
    public void Chuoi_nen_rong_khong_lam_do_vo()
    {
        var result = Classifier.Classify(new DailyPlanInputs
        {
            BtcDailyCandles = Array.Empty<Candle>(),
            SymbolDailyCandles = Array.Empty<Candle>(),
            TodayEvents = Array.Empty<ScheduledEvent>(),
        }, Settings);

        Assert.NotEmpty(result.MissingInputs);
        Assert.True(result.RiskMultiplier <= 0.5m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(59)]
    public void Moi_do_dai_chuoi_nen_deu_khong_nem(int count)
    {
        // Quét các mốc biên: 14 là ngưỡng ATR, 20 là cửa sổ cấu trúc, 59 là sát ngưỡng phân vị.
        var candles = DailyPlanFixtures.FlatClose(Enumerable.Repeat(10m, count));

        var result = Classifier.Classify(new DailyPlanInputs
        {
            BtcDailyCandles = candles,
            SymbolDailyCandles = candles,
            TodayEvents = Array.Empty<ScheduledEvent>(),
        }, Settings);

        Assert.NotNull(result);
        Assert.True(result.RiskMultiplier >= 0m);
    }

    [Fact]
    public void Nen_co_gia_bang_khong_khong_lam_chia_cho_khong()
    {
        var candles = Enumerable.Range(0, 80)
            .Select(i => new Candle(
                DailyPlanFixtures.Day0.AddDays(i), 0m, 0m, 0m, 0m, 0m,
                DailyPlanFixtures.Day0.AddDays(i + 1).AddTicks(-1)))
            .ToList();

        var result = Classifier.Classify(new DailyPlanInputs
        {
            BtcDailyCandles = candles,
            SymbolDailyCandles = candles,
            TodayEvents = Array.Empty<ScheduledEvent>(),
        }, Settings);

        Assert.NotNull(result);
        Assert.Contains(DailyPlanInputNames.AtrPercentile, result.MissingInputs);
    }
}
