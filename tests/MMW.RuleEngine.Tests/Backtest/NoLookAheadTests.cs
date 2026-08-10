using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>Kho nến giả, để test cổng dữ liệu mà không cần cơ sở dữ liệu.</summary>
internal sealed class FakeArchive : IKlineArchiveService
{
    public List<Candle> Candles { get; } = new();
    public List<Domain.Entities.FundingRateArchive> Funding { get; } = new();

    public Task<int> BackfillAsync(string s, string i, DateTime f, DateTime t, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<Candle>> GetRangeAsync(string s, string i, DateTime f, DateTime t, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Candle>>(
            Candles.Where(c => c.OpenTime >= f && c.OpenTime < t).OrderBy(c => c.OpenTime).ToList());

    public Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(string s, string i, DateTime f, DateTime t, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(DateTime, DateTime)>>(Array.Empty<(DateTime, DateTime)>());

    public Task<int> BackfillFundingAsync(string s, DateTime f, DateTime t, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<Domain.Entities.FundingRateArchive?> GetFundingAtAsync(string s, DateTime at, CancellationToken ct = default) =>
        Task.FromResult(Funding.Where(x => x.FundingTimeUtc <= at).OrderByDescending(x => x.FundingTimeUtc).FirstOrDefault());
}

/// <summary>
/// T127 — bất biến chống nhìn trước tương lai.
/// </summary>
/// <remarks>
/// Đây là hai test giá trị nhất của cả engine kiểm thử. Bỏ dòng lọc trong
/// <c>ArchiveMarketDataProvider</c> thì mọi con số kết quả đều đẹp và đều vô nghĩa — thuật toán
/// sẽ "biết" giá của tương lai mà không có triệu chứng nào lộ ra.
/// </remarks>
public class NoLookAheadTests
{
    private static readonly DateTime Start = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

    private static FakeArchive ArchiveWith(int candleCount)
    {
        var archive = new FakeArchive();
        for (var i = 0; i < candleCount; i++)
        {
            var open = Start.AddMinutes(15 * i);
            archive.Candles.Add(new Candle(open, 100m + i, 101m + i, 99m + i, 100m + i, 10m,
                open.AddMinutes(15).AddTicks(-1)));
        }
        return archive;
    }

    // ── BacktestClock ───────────────────────────────────────────────────

    [Fact]
    public void Dong_ho_tien_len_thi_chap_nhan()
    {
        var clock = new BacktestClock(Start);

        clock.Advance(Start.AddHours(1));

        Assert.Equal(Start.AddHours(1), clock.UtcNow);
    }

    [Fact]
    public void Dong_ho_dung_yen_thi_chap_nhan()
    {
        var clock = new BacktestClock(Start);

        clock.Advance(Start);

        Assert.Equal(Start, clock.UtcNow);
    }

    [Fact]
    public void Dong_ho_lui_ve_qua_khu_thi_NEM()
    {
        // Thời gian đi lùi trong một lần chạy nghĩa là vòng lặp đang dùng dữ liệu của tương lai.
        // Phải nổ ngay chứ không được âm thầm cho ra kết quả đẹp.
        var clock = new BacktestClock(Start);
        clock.Advance(Start.AddHours(2));

        Assert.Throws<InvalidOperationException>(() => clock.Advance(Start.AddHours(1)));
    }

    [Fact]
    public void Dong_ho_lui_du_mot_tich_tac_cung_nem()
    {
        var clock = new BacktestClock(Start);

        Assert.Throws<InvalidOperationException>(() => clock.Advance(Start.AddTicks(-1)));
    }

    [Fact]
    public void Dong_ho_tu_choi_gio_dia_phuong()
    {
        Assert.Throws<ArgumentException>(() => new BacktestClock(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Local)));
    }

    // ── ArchiveMarketDataProvider ───────────────────────────────────────

    [Fact]
    public async Task Khong_bao_gio_tra_nen_dong_sau_thoi_diem_hien_tai()
    {
        var archive = ArchiveWith(100);
        var clock = new BacktestClock(Start.AddHours(5));   // nến 0–19 đã đóng
        var provider = new ArchiveMarketDataProvider(archive, clock);

        var candles = await provider.GetCandlesAsync("BTCUSDT", "15m", 500);

        Assert.NotEmpty(candles);
        Assert.All(candles, c => Assert.True(c.CloseTime <= clock.UtcNow,
            $"Nến đóng lúc {c.CloseTime:o} bị trả về khi đồng hồ mới ở {clock.UtcNow:o}."));
    }

    [Fact]
    public async Task Dong_ho_tien_len_thi_thay_them_nen_moi()
    {
        var archive = ArchiveWith(100);
        var clock = new BacktestClock(Start.AddHours(2));
        var provider = new ArchiveMarketDataProvider(archive, clock);

        var before = (await provider.GetCandlesAsync("BTCUSDT", "15m", 500)).Count;
        clock.Advance(Start.AddHours(6));
        var after = (await provider.GetCandlesAsync("BTCUSDT", "15m", 500)).Count;

        Assert.True(after > before, $"Đẩy đồng hồ 4 tiếng mà số nến không tăng: {before} → {after}.");
    }

    [Fact]
    public async Task Gioi_han_dem_tren_nen_DA_DONG()
    {
        var archive = ArchiveWith(100);
        var clock = new BacktestClock(Start.AddHours(20));
        var provider = new ArchiveMarketDataProvider(archive, clock);

        var candles = await provider.GetCandlesAsync("BTCUSDT", "15m", 10);

        Assert.Equal(10, candles.Count);
        Assert.All(candles, c => Assert.True(c.CloseTime <= clock.UtcNow));
    }

    [Fact]
    public async Task Gia_hien_tai_lay_tu_nen_da_dong_gan_nhat()
    {
        var archive = ArchiveWith(100);
        var clock = new BacktestClock(Start.AddHours(5));
        var provider = new ArchiveMarketDataProvider(archive, clock);

        var lastClosed = archive.Candles.Where(c => c.CloseTime <= clock.UtcNow).OrderBy(c => c.CloseTime).Last();
        var ticker = await provider.GetTickerAsync("BTCUSDT");

        Assert.Equal(lastClosed.Close, ticker.Price);
    }

    [Fact]
    public async Task Chua_co_nen_nao_dong_thi_gia_hien_tai_NEM()
    {
        // Trả 0 ở đây sẽ lan thành mọi phép chia cho 0 ở tầng chấm điểm, và mọi tiêu chí sẽ
        // báo "thiếu dữ liệu" thay vì báo rằng kho chưa được nạp.
        var provider = new ArchiveMarketDataProvider(ArchiveWith(100), new BacktestClock(Start.AddYears(-1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTickerAsync("BTCUSDT"));
    }

    [Fact]
    public async Task Phi_von_doc_tu_kho_lich_su_va_dung_ty_le_DA_THANH_TOAN()
    {
        var archive = ArchiveWith(100);
        archive.Funding.Add(new Domain.Entities.FundingRateArchive
        {
            Symbol = "BTCUSDT", FundingTimeUtc = Start, FundingRate = 0.0003m, MarkPrice = 100m,
        });
        archive.Funding.Add(new Domain.Entities.FundingRateArchive
        {
            Symbol = "BTCUSDT", FundingTimeUtc = Start.AddHours(8), FundingRate = 0.0009m, MarkPrice = 110m,
        });

        var clock = new BacktestClock(Start.AddHours(5));   // mốc 08:00 CHƯA thanh toán
        var provider = new ArchiveMarketDataProvider(archive, clock);

        var snapshot = await provider.GetFundingAsync("BTCUSDT");

        Assert.NotNull(snapshot);
        Assert.Equal(0.0003m, snapshot!.LastFundingRate);
    }

    [Theory]
    [InlineData("openInterest")]
    [InlineData("longShort")]
    [InlineData("depth")]
    [InlineData("takerFlow")]
    public async Task Bon_nguon_khong_dung_lai_duoc_deu_tra_null(string source)
    {
        // R-003: lượng hợp đồng mở chỉ có 30 ngày, tỷ lệ mua/bán và sổ lệnh không có lịch sử
        // công khai. Trả null đẩy tiêu chí về 0 điểm theo FR-006 — đúng chiều.
        var provider = new ArchiveMarketDataProvider(ArchiveWith(100), new BacktestClock(Start.AddHours(5)));

        object? result = source switch
        {
            "openInterest" => await provider.GetOpenInterestHistAsync("BTCUSDT", "1h", 30),
            "longShort" => await provider.GetGlobalLongShortRatioAsync("BTCUSDT", "1h"),
            "depth" => await provider.GetDepthAsync("BTCUSDT"),
            _ => await provider.GetTakerBuySellRatioAsync("BTCUSDT", "1h"),
        };

        Assert.Null(result);
    }
}
