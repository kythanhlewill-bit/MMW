using MMW.Infrastructure.Exchanges.Binance;
using MMW.Infrastructure.MarketSentiment;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Toàn bộ JSON dưới đây là phản hồi THẬT chụp từ <c>fapi.binance.com</c> khi làm T001,
/// không phải mẫu tự bịa. Mẫu tự bịa chỉ chứng minh bộ bóc tách khớp với hình dung của
/// người viết test, chứ không chứng minh nó khớp với sàn.
/// </summary>
public class BinanceFuturesDataParserTests
{
    // ── Fixture thật, T001 (2026-08-02) ─────────────────────────────────

    private const string PremiumIndexJson =
        """{"symbol":"BTCUSDT","markPrice":"63086.40000000","indexPrice":"63112.91239130","estimatedSettlePrice":"63129.34966963","lastFundingRate":"0.00009145","interestRate":"0.00010000","nextFundingTime":1785686400000,"time":1785676356000}""";

    private const string FundingRateJson =
        """[{"symbol":"BTCUSDT","fundingTime":1785628800000,"fundingRate":"0.00004530","markPrice":"62792.30000000","rateType":"Regular"},{"symbol":"BTCUSDT","fundingTime":1785657600000,"fundingRate":"0.00008943","markPrice":"63468.10000000","rateType":"Regular"}]""";

    private const string OpenInterestHistJson =
        """[{"symbol":"BTCUSDT","sumOpenInterest":"109026.20500000","sumOpenInterestValue":"6869451556.04710700","CMCCirculatingSupply":"20064465.00000000","timestamp":1785672000000},{"symbol":"BTCUSDT","sumOpenInterest":"108833.19300000","sumOpenInterestValue":"6867080628.67890000","CMCCirculatingSupply":"20064465.00000000","timestamp":1785675600000}]""";

    private const string LongShortJson =
        """[{"symbol":"BTCUSDT","longAccount":"0.6561","longShortRatio":"1.9078","shortAccount":"0.3439","timestamp":1785672000000},{"symbol":"BTCUSDT","longAccount":"0.6575","longShortRatio":"1.9197","shortAccount":"0.3425","timestamp":1785675600000}]""";

    private const string TakerRatioJson =
        """[{"buySellRatio":"0.7586","sellVol":"1986.6060","buyVol":"1507.0400","timestamp":1785668400000},{"buySellRatio":"1.0267","sellVol":"1302.1260","buyVol":"1336.9250","timestamp":1785672000000}]""";

    private const string DepthJson =
        """{"lastUpdateId":11194332212850,"E":1785676357667,"T":1785676357664,"bids":[["63086.40","23.084"],["63086.30","0.025"]],"asks":[["63086.50","3.837"],["63086.60","0.076"]]}""";

    /// <summary>
    /// Phong bì lỗi PHI TIÊU CHUẨN mà Binance trả về ở HTTP 200 cho
    /// <c>/fapi/v1/fundingRate?limit=1001</c>. Không phải dạng <c>{"code":-1130,"msg":...}</c>
    /// quen thuộc, và không phải mã HTTP lỗi. Một bộ bóc tách gọi thẳng
    /// <c>Deserialize&lt;List&lt;T&gt;&gt;</c> sẽ ném hoặc trả rác (R-003 bẫy B3).
    /// </summary>
    private const string NonStandardErrorEnvelope =
        """{"status":"ERROR","type":"GENERAL","code":"99099990","errorData":"illegal params.","data":null,"subData":null,"params":null}""";

    // ── Bóc tách đúng ───────────────────────────────────────────────────

    [Fact]
    public void Boc_tach_premiumIndex()
    {
        var f = BinanceFuturesDataParser.ParseFunding(PremiumIndexJson);

        Assert.NotNull(f);
        Assert.Equal(0.00009145m, f!.LastFundingRate);
        Assert.Equal(63086.40000000m, f.MarkPrice);
        Assert.Equal(new DateTime(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc), f.NextFundingTimeUtc);
    }

    [Fact]
    public void Boc_tach_fundingRate_lich_su_va_bo_qua_truong_la()
    {
        // `rateType` là trường sàn thêm về sau. Bộ bóc tách phải bỏ qua được trường lạ,
        // nếu không thì mỗi lần Binance thêm field là một lần hệ thống chết.
        var points = BinanceFuturesDataParser.ParseFundingHistory(FundingRateJson);

        Assert.NotNull(points);
        Assert.Equal(2, points!.Count);
        Assert.Equal(0.00004530m, points[0].FundingRate);
        Assert.Equal(62792.30000000m, points[0].MarkPrice);
        Assert.Equal(new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc), points[0].FundingTimeUtc);
        Assert.Equal(new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc), points[1].FundingTimeUtc);
    }

    [Fact]
    public void Boc_tach_openInterestHist_va_bo_qua_CMCCirculatingSupply()
    {
        var s = BinanceFuturesDataParser.ParseOpenInterestHist("BTCUSDT", "1h", OpenInterestHistJson);

        Assert.NotNull(s);
        Assert.Equal(2, s!.Points.Count);
        Assert.Equal(109026.20500000m, s.Points[0].OpenInterest);
        Assert.Equal(6869451556.04710700m, s.Points[0].OpenInterestValue);
    }

    [Fact]
    public void Boc_tach_globalLongShortAccountRatio_lay_diem_moi_nhat()
    {
        var r = BinanceFuturesDataParser.ParseLongShortRatio(LongShortJson);

        Assert.NotNull(r);
        Assert.Equal(1.9197m, r!.LongShortRatioValue);
        Assert.Equal(0.6575m, r.LongAccount);
        Assert.Equal(0.3425m, r.ShortAccount);
    }

    [Fact]
    public void Boc_tach_takerlongshortRatio_du_khong_co_truong_symbol()
    {
        // Endpoint này KHÔNG trả trường `symbol` — khác hai endpoint /futures/data/* còn lại.
        var t = BinanceFuturesDataParser.ParseTakerFlow(TakerRatioJson);

        Assert.NotNull(t);
        Assert.Equal(1.0267m, t!.BuySellRatio);
        Assert.Equal(1336.9250m, t.BuyVolume);
        Assert.Equal(1302.1260m, t.SellVolume);
    }

    [Fact]
    public void Boc_tach_depth_thanh_mang_gia_va_khoi_luong()
    {
        var d = BinanceFuturesDataParser.ParseDepth(DepthJson);

        Assert.NotNull(d);
        Assert.Equal(2, d!.Bids.Count);
        Assert.Equal(63086.40m, d.Bids[0].Price);
        Assert.Equal(23.084m, d.Bids[0].Quantity);
        Assert.Equal(63086.50m, d.Asks[0].Price);
        Assert.NotNull(d.SpreadBps);
        Assert.True(d.SpreadBps > 0);
    }

    // ── Phản hồi hỏng ───────────────────────────────────────────────────

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong phai json")]
    [InlineData("null")]
    [InlineData(NonStandardErrorEnvelope)]
    public void Phan_hoi_khong_dung_dang_deu_tra_null_chu_khong_nem(string json)
    {
        // Đặc biệt chú ý phong bì lỗi phi tiêu chuẩn ở cuối danh sách: nó là ĐỐI TƯỢNG
        // trả về ở nơi đang chờ MẢNG, kèm HTTP 200. Không nhận diện được nó thì lỗi
        // sẽ đi tiếp dưới dạng dữ liệu rác.
        Assert.Null(BinanceFuturesDataParser.ParseFundingHistory(json));
        Assert.Null(BinanceFuturesDataParser.ParseOpenInterestHist("BTCUSDT", "1h", json));
        Assert.Null(BinanceFuturesDataParser.ParseLongShortRatio(json));
        Assert.Null(BinanceFuturesDataParser.ParseTakerFlow(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("khong phai json")]
    [InlineData(NonStandardErrorEnvelope)]
    public void Phan_hoi_hong_o_endpoint_dang_doi_tuong_cung_tra_null(string json)
    {
        Assert.Null(BinanceFuturesDataParser.ParseFunding(json));
        Assert.Null(BinanceFuturesDataParser.ParseDepth(json));
    }

    [Fact]
    public void Thieu_truong_bat_buoc_thi_tra_null()
    {
        Assert.Null(BinanceFuturesDataParser.ParseFunding("""{"symbol":"BTCUSDT"}"""));
        Assert.Null(BinanceFuturesDataParser.ParseDepth("""{"lastUpdateId":1}"""));
    }

    [Fact]
    public void Fear_and_greed_value_la_chuoi_va_timestamp_tinh_bang_giay()
    {
        const string json = """
            {"name":"Fear and Greed Index","data":[{"value":"27","value_classification":"Fear","timestamp":"1785628800","time_until_update":"38841"}],"metadata":{"error":null}}
            """;

        Assert.Equal(27, AlternativeMeFearGreedProvider.ParseIndex(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"value":"khong phai so"}]}""")]
    public void Fear_and_greed_hong_thi_tra_null(string json)
    {
        Assert.Null(AlternativeMeFearGreedProvider.ParseIndex(json));
    }
}
