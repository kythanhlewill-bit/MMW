using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Scoring.Criteria;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// Các tiêu chí phải đối xử với lệnh MUA và lệnh BÁN như ảnh gương của nhau.
/// </summary>
/// <remarks>
/// Bất đối xứng ở tầng chấm điểm là loại lỗi khó thấy nhất: hệ thống vẫn chạy, vẫn cho ra điểm,
/// chỉ là nó chấm một nửa số lệnh bằng một cái thước khác. Không test nào đỏ, và triệu chứng duy
/// nhất là tỉ lệ thắng chiều bán thấp hơn chiều mua một cách dai dẳng — thứ rất dễ bị đổ cho
/// "thị trường crypto có thiên hướng tăng".
/// </remarks>
public class DirectionalScoringTests
{
    private static MomentumCriterion Momentum() => new(ScoringFixtures.Indicators);

    /// <summary>
    /// Dải RSI soi gương quanh 50 theo chiều lệnh.
    /// </summary>
    /// <remarks>
    /// Dùng dải bất đối xứng [60, 100] để phép soi gương nhìn thấy được: ảnh gương của nó là
    /// [0, 40]. Với dải mặc định 45–65 thì ảnh gương là 35–55, và một chuỗi giá đơn điệu (RSI 0
    /// hoặc 100) nằm ngoài cả hai — test sẽ xanh kể cả khi phép soi gương bị gỡ bỏ.
    /// </remarks>
    [Fact]
    public void Dai_RSI_soi_guong_quanh_50_theo_chieu_lenh()
    {
        var settings = ScoringFixtures.Settings(s =>
        {
            s.RsiLowerBound = 60m;
            s.RsiUpperBound = 100m;
        });

        // Chuỗi tăng đơn điệu ⟹ RSI = 100. Chuỗi giảm đơn điệu ⟹ RSI = 0.
        var rising = ScoringFixtures.Accelerating(200);
        var falling = ScoringFixtures.Decelerating(200);

        var longOnRising = Momentum().Evaluate(
            ScoringFixtures.Context(entry: rising, direction: TradeDirection.Long, settings: settings));
        var shortOnFalling = Momentum().Evaluate(
            ScoringFixtures.Context(entry: falling, direction: TradeDirection.Short, settings: settings));

        // Mua trên chuỗi tăng: RSI 100 nằm trong [60, 100] và MACD dốc thuận.
        Assert.Equal(7, longOnRising.AwardedPoints);

        // Bán trên chuỗi giảm: RSI 0 nằm trong dải ĐÃ SOI GƯƠNG [0, 40] và MACD dốc thuận.
        // Nếu dải không soi gương, RSI 0 rơi ngoài [60, 100] và điểm chỉ còn 4.
        Assert.Equal(7, shortOnFalling.AwardedPoints);
    }

    /// <summary>
    /// Không soi gương thì lệnh bán bị chấm bằng một dải thiên mua.
    /// </summary>
    /// <remarks>
    /// Khẳng định mặt còn lại của cùng một quy tắc: động lượng giảm mạnh (RSI 0) KHÔNG được coi
    /// là động lượng lành cho một lệnh MUA. Cặp hai test này khoá cả hai chiều của phép soi
    /// gương, nên không thể làm xanh cả hai bằng cách bỏ điều kiện dải đi.
    /// </remarks>
    [Fact]
    public void Dong_luong_giam_manh_khong_phai_dong_luong_lanh_cho_lenh_mua()
    {
        var settings = ScoringFixtures.Settings(s =>
        {
            s.RsiLowerBound = 60m;
            s.RsiUpperBound = 100m;
        });

        var result = Momentum().Evaluate(ScoringFixtures.Context(
            entry: ScoringFixtures.Decelerating(200),
            direction: TradeDirection.Long,
            settings: settings));

        Assert.Equal(0, result.AwardedPoints);
    }

    // ── Xác nhận khối lượng phải có THÂN NẾN, không chỉ có dấu ──────────

    private static VolumeConfirmationCriterion Volume() => new(ScoringFixtures.Indicators);

    /// <summary>
    /// Nến doji khối lượng lớn không phải xác nhận — nó là do dự trên khối lượng lớn.
    /// </summary>
    [Fact]
    public void Nen_doji_khoi_luong_lon_khong_duoc_tinh_la_xac_nhan_chieu()
    {
        var candles = ScoringFixtures.Ramp(60);

        // Thân 0,01 trên biên độ 2 ⟹ tỉ lệ thân 0,5% — đúng dấu nhưng không nói lên điều gì.
        candles[^1] = candles[^1] with { Open = candles[^1].Close - 0.01m, Volume = 100m * 3m };

        var result = Volume().Evaluate(ScoringFixtures.Context(entry: candles));

        // Rơi về nhánh "khối lượng mạnh nhưng thân nến không xác nhận chiều lệnh" = 2 điểm,
        // không phải 5.
        Assert.Equal(2, result.AwardedPoints);
    }

    [Fact]
    public void Nen_than_day_dung_chieu_van_duoc_diem_toi_da()
    {
        var candles = ScoringFixtures.Ramp(60);

        // Thân 1,6 trên biên độ 2 ⟹ tỉ lệ thân 80%, vượt ngưỡng 50%.
        candles[^1] = candles[^1] with { Open = candles[^1].Close - 1.6m, Volume = 100m * 3m };

        Assert.Equal(5, Volume().Evaluate(ScoringFixtures.Context(entry: candles)).AwardedPoints);
    }

    [Fact]
    public void Nguong_ty_le_than_nen_doc_tu_cau_hinh()
    {
        var candles = ScoringFixtures.Ramp(60);
        candles[^1] = candles[^1] with { Open = candles[^1].Close - 0.4m, Volume = 100m * 3m };

        // Tỉ lệ thân thực tế = 0,4 / 2 = 0,2.
        var strict = ScoringFixtures.Context(
            entry: candles, settings: ScoringFixtures.Settings(s => s.MinCandleBodyRatio = 0.5m));
        var lenient = ScoringFixtures.Context(
            entry: candles, settings: ScoringFixtures.Settings(s => s.MinCandleBodyRatio = 0.1m));

        Assert.Equal(2, Volume().Evaluate(strict).AwardedPoints);
        Assert.Equal(5, Volume().Evaluate(lenient).AwardedPoints);
    }

    // ── Cờ IsDirectional phải nói THẬT (V2 §4) ──────────────────────────

    /// <summary>
    /// Tiêu chí khai báo "không đổi theo chiều" thì phải trả về ĐÚNG cùng một kết quả cho cả hai chiều.
    /// </summary>
    /// <remarks>
    /// Khai báo sai theo hướng này là lỗi im lặng và một chiều: phần điểm bị bỏ quên khỏi phép so
    /// sẽ làm biên hai chiều nhỏ đi, tức là engine chọn chiều dựa trên ít bằng chứng hơn nó tưởng —
    /// và không có gì báo, vì tổng điểm vẫn đúng.
    ///
    /// Chỉ khẳng định được MỘT chiều của phép suy luận: một tiêu chí thật sự phụ thuộc chiều vẫn
    /// có thể tình cờ cho hai kết quả giống nhau trên một bộ nến cụ thể. Vì vậy chạy trên nhiều
    /// hình dạng chuỗi giá, và có thêm một khẳng định chống-rỗng ở dưới.
    /// </remarks>
    [Fact]
    public void Tieu_chi_khai_bao_khong_doi_theo_chieu_phai_cho_cung_ket_qua()
    {
        foreach (var (name, candles) in Shapes())
        {
            foreach (var criterion in ScoringFixtures.AllCriteria().Where(c => !c.IsDirectional))
            {
                var asLong = criterion.Evaluate(Context(candles, TradeDirection.Long));
                var asShort = criterion.Evaluate(Context(candles, TradeDirection.Short));

                Assert.True(asLong == asShort,
                    $"{criterion.Key} khai báo IsDirectional = false nhưng trên chuỗi '{name}' " +
                    $"nó chấm khác nhau: mua = {asLong.AwardedPoints}, bán = {asShort.AwardedPoints}.");
            }
        }
    }

    /// <summary>
    /// Bộ gác trên phải thực sự gác được — nghĩa là phải có tiêu chí ĐANG đổi theo chiều.
    /// </summary>
    /// <remarks>
    /// Nếu mọi tiêu chí đều được khai báo là "đổi theo chiều" thì test trên xanh vì không kiểm gì
    /// cả. Khẳng định này bắt đúng trường hợp đó, và bắt luôn trường hợp tệ hơn: chấm hai chiều mà
    /// không có tiêu chí nào phân biệt được hai chiều — khi đó biên luôn bằng 0 và engine không
    /// bao giờ vào lệnh trên ngày cho cả hai chiều.
    /// </remarks>
    [Fact]
    public void Phai_co_tieu_chi_thuc_su_doi_theo_chieu()
    {
        var differing = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, candles) in Shapes())
        {
            foreach (var criterion in ScoringFixtures.AllCriteria())
            {
                if (criterion.Evaluate(Context(candles, TradeDirection.Long))
                    != criterion.Evaluate(Context(candles, TradeDirection.Short)))
                {
                    differing.Add(criterion.Key);
                }
            }
        }

        Assert.NotEmpty(differing);
        Assert.All(differing, key => Assert.True(
            ScoringFixtures.AllCriteria().Single(c => c.Key == key).IsDirectional,
            $"{key} đổi kết quả theo chiều nhưng khai báo IsDirectional = false."));
    }

    /// <summary>
    /// <c>DirectionalScore</c> chỉ cộng phần đổi theo chiều, và thang của nó suy ra từ bộ tiêu chí.
    /// </summary>
    [Fact]
    public void Diem_doi_theo_chieu_khong_gom_phan_khong_doi_theo_chieu()
    {
        var scorer = new EntryScorer(ScoringFixtures.AllCriteria());
        var criteria = ScoringFixtures.AllCriteria();

        var outcome = scorer.Score(Context(ScoringFixtures.ZigZag(260), TradeDirection.Long));

        var directionalMax = criteria
            .Where(c => c.Group != ScoreGroup.Discipline && c.IsDirectional)
            .Sum(c => c.MaxPoints);

        Assert.Equal(directionalMax, outcome.DirectionalMaxPoints);
        Assert.InRange(outcome.DirectionalScore, 0, outcome.TotalScore);
        Assert.True(outcome.DirectionalMaxPoints < outcome.TotalMaxPoints,
            "Phải có tiêu chí KHÔNG đổi theo chiều, nếu không phép so hai chiều thành phép so tổng.");
    }

    private static IEnumerable<(string Name, List<Candle> Candles)> Shapes()
    {
        yield return ("tăng đều", ScoringFixtures.Ramp(260));
        yield return ("giảm có gia tốc", ScoringFixtures.Decelerating(260));
        yield return ("răng cưa", ScoringFixtures.ZigZag(260));
    }

    private static ScoringContext Context(List<Candle> candles, TradeDirection direction) =>
        ScoringFixtures.Context(entry: candles, direction: direction);
}
