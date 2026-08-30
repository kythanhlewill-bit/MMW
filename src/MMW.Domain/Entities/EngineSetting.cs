using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Mọi ngưỡng của Deterministic Intraday Trading Engine (1:1 với TradingAccount).
/// </summary>
/// <remarks>
/// Thực thể này tồn tại để thoả Nguyên tắc I của hiến chương: không hằng số nào của thuật toán
/// được viết thẳng vào lớp tính toán. Các giá trị mặc định dưới đây là giá trị seed lấy từ
/// đặc tả — chúng là ĐIỂM XUẤT PHÁT cấu hình được, không phải hằng số của mã.
/// </remarks>
public class EngineSetting : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    /// <summary>Phiên bản luật đang được tài khoản sử dụng. Mặc định giữ V2 để migration không đổi hành vi.</summary>
    public TradingStrategyVersion StrategyVersion { get; set; } = TradingStrategyVersion.AdaptiveV2;

    // ── Ngưỡng điểm (FR-033) ────────────────────────────────────────────
    /// <summary>Dưới ngưỡng này thì không vào lệnh.</summary>
    public int MinScoreToEnter { get; set; } = 55;
    /// <summary>Từ ngưỡng này vào kích thước đầy đủ.</summary>
    public int ScoreThresholdFull { get; set; } = 70;
    /// <summary>Từ ngưỡng này vào kích thước tối đa.</summary>
    public int ScoreThresholdMax { get; set; } = 85;

    [Precision(9, 4)] public decimal SizeMultiplierLow { get; set; } = 0.5m;
    [Precision(9, 4)] public decimal SizeMultiplierFull { get; set; } = 1.0m;
    [Precision(9, 4)] public decimal SizeMultiplierMax { get; set; } = 1.5m;

    // ── Trọng số nhóm (FR-025) ──────────────────────────────────────────
    //
    // CẢNH BÁO: ba trường này KHÔNG được bất cứ đoạn mã chấm điểm nào đọc. Trần điểm mỗi nhóm
    // suy ra từ chính các IScoreCriterion đã đăng ký, không từ đây. Sửa chúng không đổi được gì.
    //
    // Và từ 2026-08-12 chúng còn nói sai: gỡ `liquidity.zone_position` làm nhóm thanh khoản rơi
    // từ 15 xuống 10 điểm, tổng thực tế là 80 chứ không phải 85. Giữ nguyên giá trị cũ có chủ ý —
    // hạ xuống 10 sẽ làm Validate() bác mọi bản ghi EngineSettings đang có (chúng đều ghi 15),
    // mà đổi lại không được gì vì không ai đọc. Muốn dọn thì phải kèm migration cho dữ liệu cũ.
    public int WeightTechnical { get; set; } = 40;
    public int WeightMarket { get; set; } = 30;
    public int WeightLiquidity { get; set; } = 15;

    // ── Tham số kỹ thuật (R-007, R-008) ─────────────────────────────────
    /// <summary>Số nến hai bên để xác nhận một điểm xoay fractal.</summary>
    public int SwingPivotBars { get; set; } = 2;
    /// <summary>Số nến tối đa chờ giá kiểm định lại vùng đã phá vỡ.</summary>
    public int RetestWindowBars { get; set; } = 6;
    /// <summary>Giá đã chạy quá số ATR này khỏi vùng xác nhận thì "vị trí vào lệnh" = 0 điểm.</summary>
    [Precision(9, 4)] public decimal MaxAtrFromConfirmation { get; set; } = 1.5m;

    public string EntryTimeframe { get; set; } = "15m";
    public string BiasTimeframe { get; set; } = "4h";

    /// <summary>Danh sách mã engine theo dõi, phân tách bằng dấu phẩy.</summary>
    public string Symbols { get; set; } = "BTCUSDT,ETHUSDT";

    /// <summary>
    /// Dừng lỗ dự phòng, tính theo bội biên độ dao động — chỉ dùng khi KHÔNG đọc được cấu trúc.
    /// </summary>
    /// <remarks>
    /// Vai trò đã đổi ở V2. Trước đây đây là công thức dừng lỗ duy nhất, và nó mù hoàn toàn với
    /// đáy/đỉnh xoay gần nhất: nếu đáy đó nằm cách giá 1,2 ATR thì dừng lỗ 1,5 ATR rơi đúng vào
    /// nơi lệnh dừng của số đông đang nằm — chỗ giá bị hút tới. Nay dừng lỗ neo vào cấu trúc và
    /// con số này chỉ là đường lui khi không có điểm xoay hợp lệ.
    /// </remarks>
    [Precision(9, 4)] public decimal StopAtrMultiple { get; set; } = 1.5m;

    // ── Dừng lỗ và mục tiêu theo cấu trúc (V2 §3) ───────────────────────

    /// <summary>Sàn khoảng cách dừng lỗ. Chống dừng lỗ dính sát khi cấu trúc quá gần giá.</summary>
    [Precision(9, 4)] public decimal StopAtrMultipleMin { get; set; } = 1.0m;

    /// <summary>
    /// Trần khoảng cách dừng lỗ. Vượt trần thì KHÔNG vào lệnh, không phải vào với size nhỏ.
    /// </summary>
    /// <remarks>
    /// Một setup mà điểm phủ định nằm cách 3,5 ATR là một setup mà ta không đọc được cấu trúc.
    /// Co size rồi vẫn vào chỉ làm cùng một sai lầm với chi phí thấp hơn.
    /// </remarks>
    [Precision(9, 4)] public decimal StopAtrMultipleMax { get; set; } = 3.0m;

    /// <summary>Khoảng đệm đặt dừng lỗ ra NGOÀI điểm xoay, tính theo bội biên độ dao động.</summary>
    [Precision(9, 4)] public decimal StopStructureBufferAtr { get; set; } = 0.30m;

    /// <summary>
    /// Sàn khoảng cách dừng lỗ tính theo PHẦN TRĂM GIÁ. Lấy giá trị lớn hơn giữa sàn này và
    /// <see cref="StopAtrMultipleMin"/>.
    /// </summary>
    /// <remarks>
    /// Sàn theo ATR một mình là chưa đủ, và đây là lý do đo được: trong tuần 10–17/08/2026 thị
    /// trường bất động (ATR ở phân vị 1–2), nên sàn ATR co lại theo và dừng lỗ ra tới <b>1–7
    /// bps</b>. Khối lượng hợp đồng bằng rủi ro chia khoảng dừng lỗ, nên dừng lỗ 1 bps nghĩa là
    /// mỗi 1R rủi ro cõng khoảng 6.700R giá trị hợp đồng — và phí tính trên con số đó.
    ///
    /// Số đo trên 1.229 phiếu đã có kết cục, gộp theo bề rộng dừng lỗ:
    /// <code>
    /// &lt;0,15%        : phí 1,538R  net −1,497R
    /// 0,15–0,30%    : phí 0,580R  net −0,659R
    /// 0,30–0,50%    : phí 0,318R  net −0,175R   ← vùng duy nhất gần hoà vốn
    /// </code>
    ///
    /// Sàn này KHÔNG tự làm lệnh có lãi. Nới dừng lỗ mà giữ nguyên mục tiêu thì R to ra và mục
    /// tiêu tính theo R co lại — <c>StructuralRoomCriterion</c> sẽ bác phiếu khi tỉ lệ tụt dưới
    /// <see cref="MinStructuralRr"/>. Tác dụng thật của nó là LOẠI những setup có cấu trúc quá
    /// nhỏ để trả nổi phí, thay vì để chúng chạy hết phễu rồi chết ở cổng chi phí.
    /// </remarks>
    [Precision(9, 4)] public decimal MinStopDistancePercent { get; set; } = 0.40m;

    /// <summary>
    /// Tỉ lệ lãi/lỗ cấu trúc tối thiểu. Dưới mức này thì không vào lệnh.
    /// </summary>
    /// <remarks>
    /// 1,6 không phải con số tròn cho đẹp — nó rút ra từ chi phí thật. Với phí taker hai chiều
    /// cộng trượt giá, một lệnh thua tốn khoảng 1,2–1,5R còn một lệnh thắng tại 1R chỉ thu về
    /// 0,6–0,8R. Dưới 1,6R thì ngay cả tỉ lệ thắng 55% cũng không đủ trả phí.
    /// </remarks>
    [Precision(9, 4)] public decimal MinStructuralRr { get; set; } = 1.6m;

    // ── Rủi ro danh mục (V2 §6) ─────────────────────────────────────────

    /// <summary>Số vị thế được mở đồng thời trên toàn tài khoản.</summary>
    public int MaxConcurrentPositions { get; set; } = 2;

    /// <summary>Tổng kích thước (theo R) cho phép trên các vị thế cùng chiều và tương quan cao.</summary>
    [Precision(9, 4)] public decimal MaxCorrelatedR { get; set; } = 1.0m;

    // ── Chọn chiều lệnh (V2 §4) ─────────────────────────────────────────

    /// <summary>Phần trăm biên độ tính từ mỗi đầu được coi là "vùng biên" của ngày đi ngang.</summary>
    [Precision(9, 4)] public decimal RangeEdgePercent { get; set; } = 25m;

    /// <summary>Mẫu hình price action cũ hơn bấy nhiêu nến thì hết hiệu lực.</summary>
    public int PatternMaxAgeBars { get; set; } = 12;

    /// <summary>
    /// Phải đo được ít nhất bấy nhiêu phần trăm thang điểm, nếu không thì veto vì quá mù.
    /// </summary>
    /// <remarks>
    /// Là TỈ LỆ chứ không phải số điểm tuyệt đối, vì thang điểm suy ra từ bộ tiêu chí đang đăng
    /// ký. Một ngưỡng tuyệt đối "70 điểm" sẽ tự động sai ngay khi ai đó thêm hoặc bớt một tiêu
    /// chí, và sai theo hướng không ai để ý: thêm tiêu chí làm ngưỡng dễ hơn, bớt tiêu chí làm
    /// mọi lệnh bị veto.
    ///
    /// Mức 75% chọn theo SÀN CỦA KIỂM THỬ LỊCH SỬ, không phải chọn cho tròn. Chạy lịch sử luôn
    /// mất 10 điểm (<c>liquidity.open_interest</c> và <c>liquidity.spread_depth</c> không dựng
    /// lại được), tức phủ 75/85 = 88%. Đặt ngưỡng 82% sẽ khiến chỉ cần mất thêm MỘT tiêu chí
    /// 6 điểm nữa là cả lượt chấm bị veto — và khi đó kiểm thử với chạy thật lại lệch nhau ở một
    /// chỗ khác, đúng thứ việc chuẩn hoá này sinh ra để xoá.
    ///
    /// 75% cho biên an toàn: mất trọn nhóm thanh khoản (15 điểm) vẫn chấm được, mất thêm cả
    /// đồng thuận khung lớn (10 điểm) thì dừng.
    /// </remarks>
    [Precision(9, 4)] public decimal MinDataCoveragePercent { get; set; } = 75m;

    // ── Thực thi lệnh (V2 §7) ───────────────────────────────────────────

    /// <summary>Lệnh limit chờ tối đa bấy nhiêu nến rồi huỷ.</summary>
    public int LimitEntryExpiryBars { get; set; } = 6;

    /// <summary>Sau bấy nhiêu nến mà lệnh chưa từng đạt <see cref="TimeStopMinR"/> thì đóng.</summary>
    public int TimeStopBars { get; set; } = 16;

    /// <summary>Mức lãi (theo R) cần đạt trước hạn thời gian để lệnh được giữ tiếp.</summary>
    [Precision(9, 4)] public decimal TimeStopMinR { get; set; } = 0.5m;

    /// <summary>Tỉ lệ thân/biên độ tối thiểu để một nến được coi là XÁC NHẬN chiều.</summary>
    /// <remarks>
    /// Không có ngưỡng này, một nến doji đóng cao hơn mở 0,01 với khối lượng gấp ba lần trung
    /// bình được tính là xác nhận đầy đủ. Nến đó thực chất là do dự trên khối lượng lớn — dấu
    /// hiệu phân phối, đúng nghĩa ngược lại với xác nhận.
    /// </remarks>
    [Precision(9, 4)] public decimal MinCandleBodyRatio { get; set; } = 0.5m;

    // ── Trigger-first, cost-aware V3 ───────────────────────────────────

    /// <summary>Retest/reclaim cũ hơn số nến này thì trigger hết hiệu lực.</summary>
    public int V3TriggerFreshBars { get; set; } = 3;

    /// <summary>Volume impulse tối thiểu so với SMA20 để TrendPullback được arm.</summary>
    [Precision(9, 4)] public decimal V3MinImpulseVolumeMultiple { get; set; } = 1.0m;

    /// <summary>Volume trung bình pullback tối đa so với volume nến impulse.</summary>
    [Precision(9, 4)] public decimal V3PullbackVolumeMaxFraction { get; set; } = 0.8m;

    /// <summary>Relative volume tối thiểu của nến range rejection.</summary>
    [Precision(9, 4)] public decimal V3RangeMinRelativeVolume { get; set; } = 1.0m;

    /// <summary>R:R ròng tối thiểu sau expected execution cost.</summary>
    [Precision(9, 4)] public decimal V3MinNetRiskReward { get; set; } = 1.5m;

    /// <summary>Expected cost không được vượt phần trăm này của gross first-target R.</summary>
    [Precision(9, 4)] public decimal V3MaxCostToTargetPercent { get; set; } = 10m;

    /// <summary>
    /// Trần TUYỆT ĐỐI cho expected execution cost, tính bằng R. Vượt mức này thì không vào lệnh,
    /// bất kể mục tiêu xa tới đâu.
    /// </summary>
    /// <remarks>
    /// <see cref="V3MaxCostToTargetPercent"/> là ngưỡng TƯƠNG ĐỐI: nó hỏi "phí chiếm bao nhiêu
    /// phần của mục tiêu". Một setup có mục tiêu đủ xa luôn trả lời được câu đó, kể cả khi phí đã
    /// vượt quá toàn bộ ngân sách rủi ro của lệnh — và đó không phải giả thuyết:
    ///
    /// <code>
    /// Phiếu #3496 (ETHUSDT, 27/08, TriangleBreakout, dừng lỗ 6,35 bps):
    ///   ExpectedCostR = 1,573   ⟵ phí một vòng ăn hết 1,57 lần ngân sách rủi ro
    ///   NetRiskReward = 8,472   ⟹ cost/target chỉ 15% ⟹ CỔNG TƯƠNG ĐỐI CHO QUA
    ///   Lệnh #63 kết cục: −1,77R (−26,32 USDT), lệnh lỗ đậm nhất của cả đợt.
    /// </code>
    ///
    /// Ngưỡng tương đối không sai, nó chỉ không đủ. Nó đo hình học của setup; cái này đo cái giá
    /// phải trả để bước vào. Một lệnh mà phí bằng 1,5R thì tỉ lệ thắng hoà vốn vọt lên trên 70%
    /// — không setup nào trong sổ này đạt được, nên câu trả lời đúng là không vào.
    ///
    /// 0,25R lấy từ chính bảng đo 2.900 phiếu: nhóm dừng lỗ ≥0,7% có phí trung bình 0,126R và là
    /// nhóm DUY NHẤT có NetR dương (+0,224); nhóm 0,4–0,7% phí 0,253R và hoà vốn (−0,046). Đặt
    /// trần ngay trên vai nhóm hoà vốn.
    /// </remarks>
    [Precision(9, 4)] public decimal MaxExpectedCostR { get; set; } = 0.25m;

    /// <summary>Lợi nhuận ròng tối thiểu cần khóa sau TP1 khi vẫn giữ runner.</summary>
    [Precision(9, 4)] public decimal V3LockedNetRMin { get; set; } = 0.25m;

    /// <summary>
    /// Sàn cho TÍCH của bốn hệ số điều chỉnh cỡ lệnh. Tích rơi xuống dưới mức này thì bị kẹp lên.
    /// </summary>
    /// <remarks>
    /// Bốn hệ số (ngày × kỷ luật × AI × dữ liệu) đều nằm trong [0, 1] và NHÂN nhau, nên chúng
    /// giảm theo cấp số nhân chứ không cộng dồn: bốn hệ số "hơi thận trọng" 0,8 cho ra 0,41, còn
    /// thêm một hệ số chất lượng setup nữa thì xuống 0,25. Không hệ số nào sai, nhưng kết quả là
    /// hai lệnh cùng một bộ luật lại mang ngân sách rủi ro lệch nhau cả chục lần.
    ///
    /// Đợt 18–28/08 đo được đúng điều đó, và cái giá của nó:
    /// <code>
    /// RiskAmount trải 2,38 → 24,99 USDT (gấp 10,5 lần) trên 35 lệnh đã đóng
    ///   #68  +9,63R  nhưng risk 2,38 ⟹ chỉ +22,90 USDT
    ///   #63  −1,77R  mà   risk 14,83 ⟹      −26,32 USDT
    /// Tổng: +2,59R  ⟹  −51,05 USDT
    /// </code>
    ///
    /// R dương mà tiền âm không phải nghịch lý — nó là hệ quả số học của việc đặt cược to vào
    /// lệnh thua và bé vào lệnh thắng. Cỡ lệnh theo mức độ tin cậy vẫn giữ (đó là thiết kế), chỉ
    /// giới hạn BIÊN ĐỘ của nó lại: 0,5 nghĩa là lệnh nhỏ nhất vẫn bằng nửa lệnh lớn nhất cùng
    /// bậc điểm, nên thắng-thua quy ra tiền còn so sánh được với nhau.
    ///
    /// Đặt 0 để tắt hẳn phép kẹp và quay về hành vi cũ.
    /// </remarks>
    [Precision(9, 4)] public decimal MinSizeMultiplierProduct { get; set; } = 0.5m;

    /// <summary>
    /// Danh sách <see cref="SetupType"/> bị cấm vào lệnh, ngăn cách bằng dấu phẩy (tên hoặc số).
    /// Rỗng nghĩa là không cấm setup nào.
    /// </summary>
    /// <remarks>
    /// Cấm ở tầng admission chứ không xoá nhánh kích hoạt: phiếu vẫn được chấm và vẫn ghi vào
    /// <c>EntryScorecards</c> với <c>VetoReason.StrategyAdmissionRejected</c>, nên cái giá của
    /// lệnh cấm vẫn đo được. Xoá nhánh đi thì không bao giờ biết được cấm như vậy là đúng hay sai.
    ///
    /// Ba setup mặc định bị cấm, theo số đo 2.900 phiếu có kết cục mô phỏng (net R sau phí) đối
    /// chiếu với 35 lệnh thật đã đóng — hai nguồn độc lập nói cùng một chiều:
    /// <code>
    /// SetupType              sim NetR TB   lệnh thật
    /// 5  RectangleRangeFade      −0,317     0 thắng / 2   (ΣR −2,61)
    /// 6  RectangleBreakout       −0,346     0 thắng / 2   (ΣR −2,61)
    /// 7  TriangleBreakout        −0,266     0 thắng / 1   (ΣR −1,77)
    /// ── giữ lại ──
    /// 8  MaPullback              −0,061     5 thắng / 15  (ΣR −2,78)
    /// 10 MaDeepPullback          +0,297     4 thắng / 10  (ΣR +14,25)  ⟵ nguồn R dương duy nhất
    /// </code>
    /// </remarks>
    public string DisabledSetupTypes { get; set; } = "RectangleRangeFade,RectangleBreakout,TriangleBreakout";

    /// <summary>
    /// Số điểm CỘNG THÊM mà một setup bán khống phải đạt so với ngưỡng vào lệnh thường.
    /// MẶC ĐỊNH 0 — cơ chế có sẵn nhưng đang TẮT, vì số liệu không ủng hộ nó.
    /// </summary>
    /// <remarks>
    /// Ghi lại cả phép đo sai lẫn phép đo đúng, vì phép đo sai rất thuyết phục.
    ///
    /// Nhìn vào lãi/lỗ của lệnh thật đã đóng thì chiều bán khống trông như một lỗ thủng:
    /// <code>
    /// Long  23 lệnh, 8 thắng, +13,53 USDT
    /// Short 12 lệnh, 3 thắng, −64,58 USDT
    /// </code>
    /// Nhưng 12 lệnh là quá ít, và quan trọng hơn: lãi/lỗ bằng tiền của đợt đó bị méo bởi chính
    /// lỗi cỡ lệnh mà <see cref="MinSizeMultiplierProduct"/> sinh ra để sửa — ngân sách rủi ro
    /// lệch nhau 10 lần nên tiền không đo được chất lượng.
    ///
    /// Đo trên mẫu lớn hơn và bằng đơn vị không méo (net R sau phí, 64 phiếu vào lệnh có kết cục
    /// mô phỏng) thì kết luận LẬT NGƯỢC:
    /// <code>
    /// Long  39 phiếu, net R trung bình −0,108
    /// Short 25 phiếu, net R trung bình +0,498   ⟵ chiều tốt hơn, không phải chiều tệ hơn
    /// </code>
    /// Sau khi đã bỏ ba setup thua và áp sàn dừng lỗ, hai chiều gần như bằng nhau và cùng dương
    /// (Long +0,449 trên 13 phiếu, Short +0,403 trên 9).
    ///
    /// Còn một lý do kỹ thuật khiến khoản thuế này đặc biệt nguy hiểm ở mức khác 0: với các bộ
    /// luật trigger-first, điểm được NÂNG lên đúng <see cref="MinScoreToEnter"/> khi trigger xác
    /// nhận, nên phần lớn phiếu nằm sát ngưỡng. Cộng thêm 5 điểm không lọc ra setup yếu — nó cắt
    /// gần như TOÀN BỘ một chiều. Đo trên 68 phiếu: thuế 5 điểm loại 27 phiếu và kéo net R trung
    /// bình từ +0,254 xuống +0,025.
    ///
    /// Giữ lại cơ chế vì nó rẻ và đã có kiểm thử; chỉ bật khi có mẫu đủ lớn nói ngược lại, và
    /// bật thì phải xem lại cả tương tác với phép nâng điểm ở trên.
    /// </remarks>
    public int ShortEntryScorePenalty { get; set; }

    /// <summary>Số giờ tối đa một vị thế trong phiên được giữ. 0 = không giới hạn.</summary>
    /// <remarks>
    /// Engine đã có dừng lỗ, chốt lời và hạn cho lệnh CHỜ, nhưng không có gì giới hạn thời gian
    /// một vị thế ĐÃ KHỚP được nằm đó. Thiếu sót ấy đắt hơn vẻ ngoài của nó, vì hai lẽ.
    ///
    /// Một, phiếu được chấm trên một cây nến 15 phút cụ thể. Sau một ngày, không còn tiêu chí
    /// nào trên phiếu ấy còn đúng — chính lập luận đã dùng để đặt hạn 60 phút cho lệnh chờ
    /// (<c>LiveOrderService.LimitEntryLifetime</c>), chỉ là chưa ai áp nó cho vị thế đã vào.
    ///
    /// Hai, vị thế mở CHIẾM CHỖ. <c>MaxConcurrentPositions</c> là 2, và cổng chống trùng vị thế
    /// chặn cả các mã cùng tài sản gốc. Ngày 29–30/08 hai vị thế nằm im (#57 mở 4 ngày, #72 mở
    /// 2 ngày) đã veto 323/476 phiếu bằng lý do <c>PositionAlreadyOpen</c> — engine chấm 476
    /// phiếu và vào 0 lệnh. Đó không phải kỷ luật, đó là tắc nghẽn.
    ///
    /// 24 giờ cho lệnh trong phiên: đủ rộng để một setup 15m đi hết đường của nó, đủ chặt để
    /// không có lệnh nào sống qua hai phiên Mỹ. Đóng bằng lệnh thị trường qua
    /// <c>ILiveOrderService.CloseOnExchangeAsync</c>, và để <c>TradeResultSyncService</c> ghi
    /// kết quả như mọi lệnh khác — không có đường ghi sổ thứ hai.
    /// </remarks>
    public int MaxHoldingHoursIntraday { get; set; } = 24;

    /// <summary>Số giờ tối đa một vị thế swing khung lớn được giữ. 0 = không giới hạn.</summary>
    /// <remarks>
    /// Rộng hơn hẳn lệnh trong phiên vì đó là lý do bộ luật swing tồn tại: mục tiêu đo bằng cấu
    /// trúc khung 4 giờ cần nhiều ngày để đi hết. 120 giờ = 5 ngày, đủ một tuần giao dịch.
    /// </remarks>
    public int MaxHoldingHoursSwing { get; set; } = 120;

    // ── Setup-specific sideways V6 ─────────────────────────────────────

    /// <summary>Số nến M15 đứng trước event dùng để dựng rectangle/triangle.</summary>
    public int V6PatternLookbackBars { get; set; } = 32;
    public int V6PatternMinTouchesPerSide { get; set; } = 2;
    [Precision(9, 4)] public decimal V6PatternContainmentPercent { get; set; } = 80m;
    [Precision(9, 4)] public decimal V6RectangleMinWidthAtr { get; set; } = 1.5m;
    [Precision(9, 4)] public decimal V6RectangleMaxWidthAtr { get; set; } = 8m;
    [Precision(9, 4)] public decimal V6RectangleMaxDriftAtr { get; set; } = 0.75m;
    [Precision(9, 4)] public decimal V6TriangleMaxEndWidthFraction { get; set; } = 0.70m;

    /// <summary>Số nến cho phép tách sweep và confirmation của Range Fade.</summary>
    public int V6RangeSweepLookbackBars { get; set; } = 2;
    [Precision(9, 4)] public decimal V6RangeConfirmationMinRelativeVolume { get; set; } = 0.80m;
    [Precision(9, 4)] public decimal V6RangeStopBufferAtr { get; set; } = 0.20m;

    public int V6BreakoutFreshBars { get; set; } = 3;
    [Precision(9, 4)] public decimal V6BreakoutBufferAtr { get; set; } = 0.10m;
    [Precision(9, 4)] public decimal V6BreakoutMinRelativeVolume { get; set; } = 1.20m;

    public int V6MinSetupQuality { get; set; } = 60;
    public int V6SetupQualityFull { get; set; } = 70;
    public int V6SetupQualityMax { get; set; } = 85;
    [Precision(9, 4)] public decimal V6QualityLowMultiplier { get; set; } = 0.50m;
    [Precision(9, 4)] public decimal V6QualityFullMultiplier { get; set; } = 0.75m;
    [Precision(9, 4)] public decimal V6QualityMaxMultiplier { get; set; } = 1.00m;
    [Precision(9, 4)] public decimal V6RangeRiskCap { get; set; } = 0.60m;
    [Precision(9, 4)] public decimal V6CompressionRiskCap { get; set; } = 0.70m;
    [Precision(9, 4)] public decimal V6TrendRiskCap { get; set; } = 1.00m;

    [Precision(9, 4)] public decimal V6RangeMinNetRiskReward { get; set; } = 1.00m;
    [Precision(9, 4)] public decimal V6BreakoutMinNetRiskReward { get; set; } = 1.30m;
    [Precision(9, 4)] public decimal V6RangeMaxCostToTargetPercent { get; set; } = 15m;
    [Precision(9, 4)] public decimal V6BreakoutMaxCostToTargetPercent { get; set; } = 12m;

    // ── V7: swing khung 4 giờ ───────────────────────────────────────────
    // Nhóm này chỉnh KHẨU VỊ của bộ luật swing. Những con số là ĐỊNH NGHĨA của nó — chu kỳ
    // EMA 20/50/200, dải Fibonacci 38,2–61,8% — nằm trong mã kèm chú thích, theo đúng ranh
    // giới mà nhóm ngưỡng chấm điểm bên dưới đã đặt ra.

    /// <summary>Số nến 4h mỗi bên để xác nhận một điểm xoay khung lớn.</summary>
    /// <remarks>
    /// Lớn hơn <see cref="SwingPivotBars"/> (2, dùng cho khung 15 phút) là có chủ ý: một điểm
    /// xoay 4h phải đại diện cho cấu trúc nhiều ngày, không phải cho một nhịp trong phiên. Với
    /// 3 nến mỗi bên, mọi pivot chỉ biết được sau 12 giờ — chậm, nhưng đó chính là thứ khiến nó
    /// đáng tin.
    /// </remarks>
    public int V7HtfPivotBars { get; set; } = 3;

    /// <summary>Số nến 4h gần nhất dùng để đọc cấu trúc xu hướng. 60 nến = 10 ngày.</summary>
    public int V7HtfStructureLookbackBars { get; set; } = 60;

    /// <summary>Nửa bề rộng của một lớp vùng giá trị, tính theo ATR khung 4h.</summary>
    [Precision(9, 4)] public decimal V7ZoneHalfWidthAtr { get; set; } = 0.25m;

    /// <summary>Số lớp hợp lưu tối thiểu để một vùng được coi là vùng giá trị dùng được.</summary>
    /// <remarks>
    /// Hai lớp là sàn, không phải sở thích. Một mình EMA50 4h thì giá cắt qua nó vài lần mỗi
    /// tuần mà chẳng nói lên điều gì; EMA50 trùng với đáy xoay trước đó mới là chỗ có người
    /// thật sự đang chờ mua.
    /// </remarks>
    public int V7MinZoneConfluence { get; set; } = 2;

    /// <summary>Đệm dừng lỗ ngoài rìa vùng giá trị, tính theo ATR khung 4h.</summary>
    [Precision(9, 4)] public decimal V7StopBufferAtr { get; set; } = 0.25m;

    /// <summary>Tỉ lệ vị thế đóng tại mục tiêu gần.</summary>
    [Precision(9, 4)] public decimal V7FirstTargetFraction { get; set; } = 0.5m;

    /// <summary>R:R tối thiểu tới mục tiêu GẦN. Dưới mức này thì chốt phần đầu không bù nổi phí.</summary>
    [Precision(9, 4)] public decimal V7MinFirstRr { get; set; } = 1.0m;

    /// <summary>
    /// R:R tối thiểu tới mục tiêu CUỐI. Đây là con số quyết định hệ này lãi hay lỗ.
    /// </summary>
    /// <remarks>
    /// Với dừng lỗ rộng theo cấu trúc 4h, tỉ lệ thắng dự kiến rơi về khoảng 35%. Ở mức đó, hoà
    /// vốn cần R:R ≈ 1,9 và đó là TRƯỚC phí. 2,5 là biên an toàn, không phải mong muốn.
    /// </remarks>
    [Precision(9, 4)] public decimal V7MinRunnerRr { get; set; } = 2.5m;

    /// <summary>Số nến 4h mỗi bên dùng để tìm pivot khi kéo dừng lỗ. 0 = không kéo.</summary>
    public int V7TrailPivotBars { get; set; } = 2;

    /// <summary>Số nến 4h tối đa kể từ lúc cấu trúc xác nhận. Quá thì nhịp không còn tươi.</summary>
    public int V7MaxSetupAgeBars { get; set; } = 12;

    /// <summary>Số lệnh swing được mở cùng lúc, đếm riêng với lệnh ngắn.</summary>
    /// <remarks>
    /// Đếm riêng vì hai nhóm tiêu hai loại ngân sách khác nhau: lệnh swing giữ nhiều ngày và
    /// chiếm chỗ lâu, còn lệnh ngắn xoay vòng trong phiên. Dùng chung một hạn mức thì nhóm giữ
    /// lâu sẽ khoá cửa nhóm kia suốt cả tuần.
    /// </remarks>
    public int V7MaxConcurrentSwingPositions { get; set; } = 2;

    /// <summary>Các mã engine theo dõi, đã tách và chuẩn hoá.</summary>
    public IReadOnlyList<string> SymbolList() =>
        (Symbols ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // ── Ngưỡng chấm điểm (FR-026 → FR-032) ──────────────────────────────
    // Đây là các ngưỡng ĐỂ CHỈNH, khác với những con số là ĐỊNH NGHĨA (chu kỳ EMA 20/50/200,
    // chu kỳ trung bình khối lượng 20) vốn nằm trong mã kèm chú thích. Nguyên tắc I áp cho
    // nhóm thứ nhất: chỉnh chúng là chỉnh khẩu vị, không phải sửa thuật toán.

    /// <summary>Biên dưới của dải RSI được coi là động lượng lành.</summary>
    [Precision(9, 4)] public decimal RsiLowerBound { get; set; } = 45m;
    [Precision(9, 4)] public decimal RsiUpperBound { get; set; } = 65m;

    /// <summary>Khối lượng nến phá vỡ phải vượt bấy nhiêu lần trung bình 20 nến.</summary>
    [Precision(9, 4)] public decimal VolumeBreakoutMultiple { get; set; } = 1.5m;

    /// <summary>Phân vị biến động cho điểm tối đa nằm trong dải này.</summary>
    [Precision(9, 4)] public decimal VolatilitySweetSpotLow { get; set; } = 30m;
    [Precision(9, 4)] public decimal VolatilitySweetSpotHigh { get; set; } = 70m;

    /// <summary>Phí vốn tuyệt đối từ mức này trở lên là "đám đông đã chật một phía".</summary>
    [Precision(9, 8)] public decimal ExtremeFundingRate { get; set; } = 0.0005m;

    /// <summary>Thay đổi lượng hợp đồng mở (%) từ mức này là tín hiệu mạnh.</summary>
    [Precision(9, 4)] public decimal OpenInterestStrongChangePercent { get; set; } = 3m;

    /// <summary>Chênh lệch mua-bán tối đa (điểm cơ bản) còn được điểm tối đa.</summary>
    [Precision(9, 4)] public decimal MaxSpreadBps { get; set; } = 2m;

    /// <summary>Tương quan với mã dẫn dắt từ mức này trở lên là đồng pha rõ.</summary>
    [Precision(9, 4)] public decimal LeaderCorrelationStrong { get; set; } = 0.7m;

    // ── Chuyển sang thống kê cá nhân (FR-030) ───────────────────────────
    /// <summary>Dưới số lệnh đã đóng này thì dùng bảng phiên chuẩn, không dùng thống kê giờ cá nhân.</summary>
    public int PersonalStatsMinClosedTrades { get; set; } = 50;
    public int WorstHoursPenalty { get; set; } = 10;

    /// <summary>
    /// Cỡ mẫu "ảo" kéo điểm phiên cá nhân về bảng chuẩn. Càng lớn thì càng cần nhiều lệnh
    /// thật mới dịch được điểm khỏi giá trị chuẩn.
    /// </summary>
    /// <remarks>
    /// Không có hệ số này thì một khung giờ có đúng một lệnh thua sẽ nhận điểm 0 và bị cấm cửa
    /// vĩnh viễn dựa trên một mẫu duy nhất. Đủ 50 lệnh trải trên 6 khung phiên nghĩa là mỗi
    /// khung chỉ khoảng 8 lệnh — vẫn quá ít để tin tuyệt đối.
    /// </remarks>
    public int SessionStatsSmoothingTrades { get; set; } = 10;

    // ── Kỷ luật (FR-035) ────────────────────────────────────────────────
    public int LossStreakSizeHalveAt { get; set; } = 2;

    /// <summary>Hệ số nhân kích thước khi chạm ngưỡng chuỗi thua. PHẢI ≤ 1.0.</summary>
    [Precision(9, 4)] public decimal LossStreakSizeMultiplier { get; set; } = 0.5m;

    /// <summary>
    /// Tách khỏi <see cref="RiskSetting.RevengeTradeWindowMinutes"/> (30) có chủ ý:
    /// đây là ngưỡng CHẶN lệnh, còn kia là ngưỡng CẢNH BÁO. Gộp chung sẽ buộc phải
    /// chọn một trong hai vai trò và làm hỏng vai còn lại.
    /// </summary>
    public int RevengeBlockMinutes { get; set; } = 15;

    [Precision(9, 4)] public decimal OversizeBlockMultiple { get; set; } = 1.5m;
    public int OversizeLookbackTrades { get; set; } = 20;

    // ── Xử lý vị thế trước cửa sổ chặn (FR-013) ─────────────────────────
    /// <summary>Bắt đầu làm phẳng vị thế khi cửa sổ chặn còn cách bấy nhiêu phút.</summary>
    public int BlackoutLeadMinutes { get; set; } = 15;

    /// <summary>Lãi từ mức này (tính theo R) thì kéo dừng lỗ về hoà vốn; dưới mức này thì đóng bớt.</summary>
    [Precision(9, 4)] public decimal BlackoutBreakevenAtR { get; set; } = 0.5m;

    /// <summary>Phần trăm khối lượng phải đóng khi vị thế chưa đủ lãi để kéo về hoà vốn.</summary>
    [Precision(9, 4)] public decimal BlackoutPartialClosePercent { get; set; } = 50m;

    /// <summary>Đồng hồ máy chủ lệch sàn quá bấy nhiêu giây thì cảnh báo — mọi cửa sổ chặn đều sai theo.</summary>
    public int ClockDriftToleranceSeconds { get; set; } = 30;

    // ── Lớp AI (FR-011, FR-044) ─────────────────────────────────────────
    /// <summary>Trần độ dài một cửa sổ chặn do AI đề xuất. Dài hơn thì cắt về đây.</summary>
    public int AiBlackoutMaxMinutes { get; set; } = 120;
    public int AiContextDefaultTtlMinutes { get; set; } = 240;
    // 14 giữ cửa sổ 24 giờ xấu nhất vắt qua hai ngày UTC dưới 30 lượt (14 + 14 + Daily Brief).
    public int AiMaxNewsCallsPerDay { get; set; } = 14;
    public int AiMaxNewsCallsPerRun { get; set; } = 3;

    // ── Kiểm thử lịch sử (R-012) ────────────────────────────────────────
    [Precision(9, 4)] public decimal BacktestTakerFeePercent { get; set; } = 0.05m;

    /// <summary>
    /// Phí maker, dùng cho chân vào lệnh limit và chân chốt lời limit.
    /// </summary>
    /// <remarks>
    /// Tách khỏi phí taker là điều kiện để đo được lợi ích của V2. Chi phí tính theo R tỉ lệ
    /// NGHỊCH với độ rộng dừng lỗ, nên với dừng lỗ hẹp thì phí chiếm phần rất lớn: ở ATR bằng
    /// 0,18% giá và dừng lỗ 1,5 ATR, phí taker một chiều đã là 0,185R. Ép mọi chân qua phí taker
    /// sẽ phạt đúng cái cải tiến mà kiểm thử đang cần đo.
    /// </remarks>
    [Precision(9, 4)] public decimal BacktestMakerFeePercent { get; set; } = 0.02m;

    [Precision(9, 4)] public decimal BacktestEntrySlippageBps { get; set; } = 1m;
    [Precision(9, 4)] public decimal BacktestStopSlippageBps { get; set; } = 3m;

    /// <summary>
    /// Lệnh limit chỉ được coi là khớp khi giá đi XUYÊN QUA mức, không chỉ chạm tới.
    /// </summary>
    /// <remarks>
    /// Dữ liệu nến không có sổ lệnh, nên vị trí hàng đợi phải được mô hình hoá bằng giả định.
    /// Hai giả định biên:
    ///
    /// • <c>false</c> — LẠC QUAN: chạm là khớp. Giả định lệnh của mình luôn đứng đầu hàng đợi.
    /// • <c>true</c> — THẬN TRỌNG: phải xuyên qua. Giả định phải có người bán/mua hết phần
    ///   xếp trước mình thì mới tới lượt.
    ///
    /// Sự thật nằm giữa hai mức và không dựng lại được từ nến. Vì vậy điều kiện chấp nhận của V2
    /// đòi kết quả phải đứng vững ở CẢ HAI: một cải tiến chỉ tồn tại ở mô hình lạc quan là một
    /// cải tiến của giả định, không phải của chiến lược.
    ///
    /// Chỉ áp cho lệnh LIMIT. Dừng lỗ là lệnh stop-market: chạm mức là kích hoạt, không cần xuyên.
    /// </remarks>
    public bool BacktestLimitFillRequiresThrough { get; set; } = true;

    // ── Chế độ so sánh song song (FR-059) ───────────────────────────────
    public bool ShadowAiComparisonEnabled { get; set; } = true;

    public ICollection<SessionQualityRow> SessionQualityRows { get; set; } = new List<SessionQualityRow>();
    public ICollection<BlackoutRule> BlackoutRules { get; set; } = new List<BlackoutRule>();

    /// <summary>
    /// Bản sao nông, dùng để dựng một cấu hình phái sinh trong bộ nhớ mà không đụng CSDL.
    /// </summary>
    /// <remarks>
    /// Đây là thứ cho phép chạy hai bộ luật trên cùng một tài khoản mà không cần thêm cột nào:
    /// mọi knob của bộ luật swing (V7*) ĐÃ là cột trên chính dòng này, nên sao chép rồi lật
    /// StrategyVersion là ra một cấu hình swing đầy đủ và hợp lệ.
    ///
    /// Bản sao KHÔNG được đưa cho EF theo dõi — nó mang cùng khoá chính với bản gốc, và lưu nó
    /// sẽ ghi đè cấu hình thật của tài khoản. Mọi nơi dùng nó đều đọc bằng AsNoTracking.
    /// </remarks>
    public EngineSetting ShallowCopy() => (EngineSetting)MemberwiseClone();

    /// <summary>
    /// Kiểm tra toàn bộ ràng buộc cấu hình. Rỗng nghĩa là hợp lệ.
    /// </summary>
    /// <remarks>
    /// PHẢI gọi khi LƯU, không phải khi đọc. Một cấu hình sai không làm hệ thống lỗi —
    /// nó làm hệ thống chạy sai trong im lặng. Bảng phiên thủng một giờ sẽ biến thành
    /// "thiếu dữ liệu ⟹ 0 điểm" đúng vào giờ đó, mỗi ngày, và không ai biết.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // ── Ngưỡng điểm phải không giảm dần ─────────────────────────────
        if (MinScoreToEnter > ScoreThresholdFull)
            errors.Add($"MinScoreToEnter ({MinScoreToEnter}) không được lớn hơn ScoreThresholdFull ({ScoreThresholdFull}).");

        if (ScoreThresholdFull > ScoreThresholdMax)
            errors.Add($"ScoreThresholdFull ({ScoreThresholdFull}) không được lớn hơn ScoreThresholdMax ({ScoreThresholdMax}).");

        if (SizeMultiplierLow > SizeMultiplierFull || SizeMultiplierFull > SizeMultiplierMax)
            errors.Add($"SizeMultiplier phải không giảm dần: {SizeMultiplierLow} / {SizeMultiplierFull} / {SizeMultiplierMax}.");

        // ── Trọng số nhóm ───────────────────────────────────────────────
        var weightSum = WeightTechnical + WeightMarket + WeightLiquidity;
        if (weightSum != 85)
            errors.Add($"Tổng ba trọng số nhóm phải bằng 85 (hiện {weightSum}); 15 điểm còn lại thuộc nhóm kỷ luật chỉ-trừ.");

        // Hệ số kéo về 0 sẽ chia cho 0 khi khung giờ chưa có lệnh nào; âm thì đảo dấu công thức.
        if (SessionStatsSmoothingTrades < 1)
            errors.Add($"SessionStatsSmoothingTrades ({SessionStatsSmoothingTrades}) phải từ 1 trở lên.");

        if (AiBlackoutMaxMinutes < 0)
            errors.Add($"AiBlackoutMaxMinutes ({AiBlackoutMaxMinutes}) không được âm.");

        if (AiMaxNewsCallsPerDay is < 0 or > 28)
            errors.Add($"AiMaxNewsCallsPerDay ({AiMaxNewsCallsPerDay}) phải nằm trong 0–28 để tổng ngân sách AI dưới 30/ngày.");

        if (AiMaxNewsCallsPerRun < 0)
            errors.Add($"AiMaxNewsCallsPerRun ({AiMaxNewsCallsPerRun}) không được âm.");

        // ── Dừng lỗ cấu trúc ────────────────────────────────────────────
        if (StopAtrMultipleMin <= 0m)
            errors.Add($"StopAtrMultipleMin ({StopAtrMultipleMin}) phải lớn hơn 0.");

        if (StopAtrMultipleMin > StopAtrMultipleMax)
            errors.Add($"StopAtrMultipleMin ({StopAtrMultipleMin}) không được lớn hơn StopAtrMultipleMax ({StopAtrMultipleMax}).");

        // Dừng lỗ dự phòng nằm ngoài [min, max] nghĩa là đường lui vi phạm chính ràng buộc mà
        // đường chính phải tuân theo — im lặng cho ra những lệnh không ai duyệt.
        if (StopAtrMultiple < StopAtrMultipleMin || StopAtrMultiple > StopAtrMultipleMax)
            errors.Add($"StopAtrMultiple ({StopAtrMultiple}) phải nằm trong [{StopAtrMultipleMin}, {StopAtrMultipleMax}].");

        if (MinStopDistancePercent is < 0m or > 5m)
            errors.Add($"MinStopDistancePercent ({MinStopDistancePercent}) phải nằm trong [0, 5].");

        if (StopStructureBufferAtr < 0m)
            errors.Add($"StopStructureBufferAtr ({StopStructureBufferAtr}) không được âm.");

        // Dưới 1.0 thì mục tiêu gần hơn dừng lỗ — không cấu hình nào biện minh được điều đó.
        if (MinStructuralRr < 1.0m)
            errors.Add($"MinStructuralRr ({MinStructuralRr}) phải từ 1.0 trở lên.");

        // ── Rủi ro danh mục và chọn chiều ───────────────────────────────
        if (MaxConcurrentPositions < 1)
            errors.Add($"MaxConcurrentPositions ({MaxConcurrentPositions}) phải từ 1 trở lên.");

        if (MaxCorrelatedR <= 0m)
            errors.Add($"MaxCorrelatedR ({MaxCorrelatedR}) phải lớn hơn 0.");

        // Từ 50% trở lên thì hai "vùng biên" phủ kín biên độ và ràng buộc mất hết tác dụng.
        if (RangeEdgePercent is <= 0m or >= 50m)
            errors.Add($"RangeEdgePercent ({RangeEdgePercent}) phải nằm trong khoảng (0, 50).");

        if (PatternMaxAgeBars < 1)
            errors.Add($"PatternMaxAgeBars ({PatternMaxAgeBars}) phải từ 1 trở lên.");

        if (MinDataCoveragePercent is < 0m or > 100m)
            errors.Add($"MinDataCoveragePercent ({MinDataCoveragePercent}) phải nằm trong 0–100.");

        // ── Thực thi và chi phí ─────────────────────────────────────────
        if (LimitEntryExpiryBars < 1)
            errors.Add($"LimitEntryExpiryBars ({LimitEntryExpiryBars}) phải từ 1 trở lên.");

        if (TimeStopBars < 1)
            errors.Add($"TimeStopBars ({TimeStopBars}) phải từ 1 trở lên.");

        if (TimeStopMinR <= 0m)
            errors.Add($"TimeStopMinR ({TimeStopMinR}) phải lớn hơn 0.");

        if (MinCandleBodyRatio is < 0m or > 1m)
            errors.Add($"MinCandleBodyRatio ({MinCandleBodyRatio}) phải nằm trong 0–1.");

        if (!Enum.IsDefined(StrategyVersion))
            errors.Add($"StrategyVersion ({StrategyVersion}) không được hỗ trợ.");

        if (V3TriggerFreshBars < 1)
            errors.Add($"V3TriggerFreshBars ({V3TriggerFreshBars}) phải từ 1 trở lên.");

        if (V3MinImpulseVolumeMultiple <= 0m)
            errors.Add($"V3MinImpulseVolumeMultiple ({V3MinImpulseVolumeMultiple}) phải lớn hơn 0.");

        if (V3PullbackVolumeMaxFraction is <= 0m or > 1m)
            errors.Add($"V3PullbackVolumeMaxFraction ({V3PullbackVolumeMaxFraction}) phải nằm trong (0, 1].");

        if (V3RangeMinRelativeVolume <= 0m)
            errors.Add($"V3RangeMinRelativeVolume ({V3RangeMinRelativeVolume}) phải lớn hơn 0.");

        if (V3MinNetRiskReward < 1m)
            errors.Add($"V3MinNetRiskReward ({V3MinNetRiskReward}) phải từ 1 trở lên.");

        if (V3MaxCostToTargetPercent is <= 0m or > 100m)
            errors.Add($"V3MaxCostToTargetPercent ({V3MaxCostToTargetPercent}) phải nằm trong (0, 100].");

        // Trần trên là 1R chứ không phải một số lớn tuỳ ý: phí bằng đúng ngân sách rủi ro nghĩa
        // là lệnh phải đi hết 1R chỉ để về mo. Cho phép cấu hình cao hơn thế là cho phép tắt
        // cổng bằng cách gõ một con số to.
        if (MaxExpectedCostR is <= 0m or > 1m)
            errors.Add($"MaxExpectedCostR ({MaxExpectedCostR}) phải nằm trong (0, 1].");

        if (MinSizeMultiplierProduct is < 0m or > 1m)
            errors.Add($"MinSizeMultiplierProduct ({MinSizeMultiplierProduct}) phải nằm trong [0, 1].");

        if (ShortEntryScorePenalty is < 0 or > 50)
            errors.Add($"ShortEntryScorePenalty ({ShortEntryScorePenalty}) phải nằm trong [0, 50].");

        if (MaxHoldingHoursIntraday is < 0 or > 720)
            errors.Add($"MaxHoldingHoursIntraday ({MaxHoldingHoursIntraday}) phải nằm trong [0, 720].");

        if (MaxHoldingHoursSwing is < 0 or > 720)
            errors.Add($"MaxHoldingHoursSwing ({MaxHoldingHoursSwing}) phải nằm trong [0, 720].");

        // Một tên gõ sai phải nổ ra ở đây. Bỏ qua âm thầm nghĩa là setup vẫn chạy trong khi cấu
        // hình nói đã cấm — và không ai phát hiện cho tới lúc đọc lại sổ lệnh.
        ParseDisabledSetups(DisabledSetupTypes, out var invalidSetupTokens);
        foreach (var token in invalidSetupTokens)
            errors.Add($"DisabledSetupTypes chứa giá trị không hợp lệ: \"{token}\".");

        if (V3LockedNetRMin < 0m)
            errors.Add($"V3LockedNetRMin ({V3LockedNetRMin}) không được âm.");

        if (V6PatternLookbackBars < 12)
            errors.Add($"V6PatternLookbackBars ({V6PatternLookbackBars}) phải từ 12 trở lên.");
        if (V6PatternMinTouchesPerSide < 2)
            errors.Add($"V6PatternMinTouchesPerSide ({V6PatternMinTouchesPerSide}) phải từ 2 trở lên.");
        if (V6PatternContainmentPercent is < 50m or > 100m)
            errors.Add($"V6PatternContainmentPercent ({V6PatternContainmentPercent}) phải nằm trong 50–100.");
        if (V6RectangleMinWidthAtr <= 0m || V6RectangleMinWidthAtr >= V6RectangleMaxWidthAtr)
            errors.Add("Dải V6RectangleMinWidthAtr/V6RectangleMaxWidthAtr không hợp lệ.");
        if (V6RectangleMaxDriftAtr < 0m)
            errors.Add($"V6RectangleMaxDriftAtr ({V6RectangleMaxDriftAtr}) không được âm.");
        if (V6TriangleMaxEndWidthFraction is <= 0m or >= 1m)
            errors.Add($"V6TriangleMaxEndWidthFraction ({V6TriangleMaxEndWidthFraction}) phải nằm trong (0, 1).");
        if (V6RangeSweepLookbackBars is < 1 or > 6)
            errors.Add($"V6RangeSweepLookbackBars ({V6RangeSweepLookbackBars}) phải nằm trong 1–6.");
        if (V6RangeConfirmationMinRelativeVolume <= 0m || V6RangeStopBufferAtr < 0m)
            errors.Add("Volume confirmation V6 phải dương và stop buffer V6 không được âm.");
        if (V6BreakoutFreshBars is < 1 or > 12 || V6BreakoutBufferAtr < 0m || V6BreakoutMinRelativeVolume <= 0m)
            errors.Add("Cấu hình breakout V6 không hợp lệ.");
        if (V6MinSetupQuality is < 0 or > 100
            || V6SetupQualityFull < V6MinSetupQuality
            || V6SetupQualityMax < V6SetupQualityFull
            || V6SetupQualityMax > 100)
            errors.Add("Các ngưỡng setup quality V6 phải tăng dần trong 0–100.");
        if (new[] { V6QualityLowMultiplier, V6QualityFullMultiplier, V6QualityMaxMultiplier,
                V6RangeRiskCap, V6CompressionRiskCap, V6TrendRiskCap }.Any(x => x is < 0m or > 1m))
            errors.Add("Mọi multiplier/risk cap V6 phải nằm trong 0–1.");
        if (V6RangeMinNetRiskReward <= 0m || V6BreakoutMinNetRiskReward <= 0m)
            errors.Add("Net R:R tối thiểu V6 phải lớn hơn 0.");
        if (V6RangeMaxCostToTargetPercent is <= 0m or > 100m
            || V6BreakoutMaxCostToTargetPercent is <= 0m or > 100m)
            errors.Add("Cost/target V6 phải nằm trong (0, 100].");

        // Maker đắt hơn taker là dấu hiệu điền nhầm hai ô cho nhau — và nó sẽ khiến kiểm thử
        // kết luận rằng lệnh limit tệ hơn lệnh thị trường.
        if (BacktestMakerFeePercent < 0m || BacktestMakerFeePercent > BacktestTakerFeePercent)
            errors.Add($"BacktestMakerFeePercent ({BacktestMakerFeePercent}) phải không âm và không vượt BacktestTakerFeePercent ({BacktestTakerFeePercent}).");

        errors.AddRange(ValidateSessionTable());
        errors.AddRange(ValidateBlackoutRules());
        return errors;
    }

    /// <summary>
    /// Tập <see cref="SetupType"/> bị cấm, đọc từ <see cref="DisabledSetupTypes"/>.
    /// </summary>
    /// <remarks>
    /// Nhận cả tên (<c>RectangleBreakout</c>) lẫn số (<c>6</c>) để cấu hình sửa được bằng tay
    /// trong DB mà không cần tra bảng enum. Token nào không phân giải được thì đi ra qua
    /// <paramref name="invalid"/> — người gọi quyết định báo lỗi hay bỏ qua; ở đây KHÔNG nuốt
    /// lỗi, vì một token gõ sai bị bỏ qua chính là một setup tưởng đã cấm mà vẫn chạy.
    /// </remarks>
    public static IReadOnlySet<SetupType> ParseDisabledSetups(string? raw, out IReadOnlyList<string> invalid)
    {
        var disabled = new HashSet<SetupType>();
        var bad = new List<string>();

        foreach (var token in (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SetupType>(token, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                disabled.Add(parsed);
            }
            else
            {
                bad.Add(token);
            }
        }

        invalid = bad;
        return disabled;
    }

    private IEnumerable<string> ValidateSessionTable()
    {
        var rows = SessionQualityRows.OrderBy(r => r.FromHourUtc).ToList();

        if (rows.Count == 0)
        {
            yield return "Bảng chất lượng phiên rỗng: phải phủ kín 0–24.";
            yield break;
        }

        foreach (var r in rows.Where(r => r.Score is < 0 or > 6))
            yield return $"Điểm phiên của khoảng {r.FromHourUtc}–{r.ToHourUtc} là {r.Score}, phải nằm trong 0–6.";

        foreach (var r in rows.Where(r => r.FromHourUtc >= r.ToHourUtc))
            yield return $"Khoảng phiên {r.FromHourUtc}–{r.ToHourUtc} không hợp lệ: giờ bắt đầu phải nhỏ hơn giờ kết thúc.";

        // Đi tuần tự từ 0: mỗi khoảng phải nối đúng vào điểm kết thúc của khoảng trước.
        // Cách này bắt được cả lỗ hổng lẫn chồng lấn bằng cùng một phép kiểm tra.
        var cursor = 0;
        foreach (var r in rows)
        {
            if (r.FromHourUtc != cursor)
            {
                yield return r.FromHourUtc > cursor
                    ? $"Bảng phiên không phủ kín: hở từ giờ {cursor} đến {r.FromHourUtc}."
                    : $"Bảng phiên chồng lấn tại giờ {r.FromHourUtc} (khoảng trước đã kết thúc ở {cursor}).";
                yield break;
            }
            cursor = r.ToHourUtc;
        }

        if (cursor != 24)
            yield return $"Bảng phiên không phủ kín: kết thúc ở giờ {cursor}, phải là 24.";
    }

    private IEnumerable<string> ValidateBlackoutRules()
    {
        foreach (var g in BlackoutRules.GroupBy(r => r.EventKind).Where(g => g.Count() > 1))
            yield return $"Luật chặn trùng loại sự kiện {g.Key}: có {g.Count()} dòng, chỉ được 1.";

        foreach (var r in BlackoutRules.Where(r => r.MinutesBefore < 0 || r.MinutesAfter < 0))
            yield return $"Cửa sổ chặn của {r.EventKind} có giá trị âm ({r.MinutesBefore}/{r.MinutesAfter}).";
    }
}
