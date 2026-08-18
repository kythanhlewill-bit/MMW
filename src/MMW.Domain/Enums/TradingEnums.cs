namespace MMW.Domain.Enums;

/// <summary>Sàn/nguồn tài khoản giao dịch.</summary>
public enum Broker
{
    Binance = 1,
    Bybit = 2,
    Okx = 3,
    Demo = 98,
    Other = 99,
}

public enum TradeDirection
{
    Long = 1,
    Short = 2,
}

public enum TradeStatus
{
    /// <summary>Mới lên kế hoạch, chưa vào lệnh (dùng cho "Trade Gate").</summary>
    Planned = 1,
    Open = 2,
    Closed = 3,
    Cancelled = 4,
}

public enum TradeSource
{
    Manual = 1,
    Import = 2,
    Api = 3,
}

/// <summary>Loại lệnh đặt.</summary>
public enum OrderType
{
    Market = 1,
    Limit = 2,
    StopLimit = 3,
}

/// <summary>Trạng thái gửi lệnh THẬT lên sàn (live trading).</summary>
public enum LiveOrderStatus
{
    /// <summary>Chưa gửi (lệnh chỉ ghi nhật ký, không live).</summary>
    None = 0,
    /// <summary>Đã gửi lên sàn, chờ khớp.</summary>
    Submitted = 1,
    /// <summary>Đã khớp.</summary>
    Filled = 2,
    /// <summary>Bị chặn trước khi gửi (vi phạm rule/cap).</summary>
    Blocked = 3,
    /// <summary>Lỗi khi gửi lên sàn.</summary>
    Error = 4,
    /// <summary>Đã huỷ.</summary>
    Canceled = 5,
    /// <summary>Entry đã vào sàn nhưng SL/TP đặt lỗi — đang chờ job retry đặt lại.</summary>
    SltpPending = 6,

    /// <summary>
    /// Lệnh chờ maker đang nằm trên sổ, CHƯA khớp — nên chưa có vị thế nào để bảo vệ.
    /// </summary>
    /// <remarks>
    /// Phải tách khỏi <see cref="Submitted"/> vì hai trạng thái đòi hỏi hành động trái ngược:
    /// lệnh thị trường đã gửi thì vị thế coi như có và SL/TP phải đặt NGAY, còn lệnh chờ thì
    /// chưa có gì để đóng. Đặt STOP_MARKET/TAKE_PROFIT_MARKET kèm closePosition lên một vị thế
    /// chưa tồn tại là tự đặt bẫy: giá chạm mức dừng lỗ sẽ kích hoạt và tiêu mất lệnh bảo vệ
    /// trong khi ta còn chưa vào, để rồi lúc lệnh chờ khớp thật thì vị thế trần trụi.
    /// </remarks>
    EntryPending = 7,
}

public enum TradeOutcome
{
    Win = 1,
    Loss = 2,
    BreakEven = 3,
}

/// <summary>Trạng thái tâm lý tự đánh giá — cốt lõi cho journaling kỷ luật.</summary>
public enum EmotionState
{
    Unknown = 0,
    Calm = 1,
    Confident = 2,
    Disciplined = 3,
    Fomo = 4,
    Revenge = 5,
    Fearful = 6,
    Greedy = 7,
    Bored = 8,
    Tilted = 9,
}

/// <summary>Nhãn gắn vào lệnh để review sau.</summary>
public enum TagKind
{
    Mistake = 1,
    Setup = 2,
    Condition = 3,
    Other = 99,
}

/// <summary>Thiên hướng thị trường suy ra từ indicator (deterministic).</summary>
public enum MarketBias
{
    Bearish = -1,
    Neutral = 0,
    Bullish = 1,
}

/// <summary>Phân loại cờ: vi phạm rule cứng hay hành vi phát hiện được.</summary>
public enum FlagCategory
{
    RuleViolation = 1,
    Behavior = 2,
}

public enum FlagSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>Loại cờ. 1xx = Rule Engine, 2xx = Behavior detection.</summary>
public enum FlagType
{
    // --- Rule violations (deterministic) ---
    RiskExceeded = 100,
    LowRiskReward = 101,
    NoStopLoss = 102,
    MaxTradesPerDayExceeded = 103,
    DailyLossLimitExceeded = 104,
    PositionSizeExceeded = 105,

    // --- Behaviors ---
    RevengeTrade = 200,
    Overtrade = 201,
    Tilt = 202,
    LossStreak = 203,
    OversizedAfterLoss = 204,
}

// ─────────────────────────────────────────────────────────────────────────
// Deterministic Intraday Trading Engine — 3xx
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phiên bản luật ra quyết định. Giá trị nằm trong cấu hình tài khoản để cùng một mã có thể
/// chạy live, shadow và backtest theo đúng phiên bản đã được đóng băng.
/// </summary>
public enum TradingStrategyVersion
{
    AdaptiveV2 = 2,
    TriggerFirstV3 = 3,
    CalibratedV5 = 5,
    AdaptiveSidewaysV6 = 6,
}

public static class TradingStrategyVersionExtensions
{
    public static bool UsesTriggerFirst(this TradingStrategyVersion version) => version is
        TradingStrategyVersion.TriggerFirstV3
        or TradingStrategyVersion.CalibratedV5
        or TradingStrategyVersion.AdaptiveSidewaysV6;

    public static bool UsesV5Admission(this TradingStrategyVersion version) => version is
        TradingStrategyVersion.CalibratedV5
        or TradingStrategyVersion.AdaptiveSidewaysV6;

    public static bool UsesSidewaysV6(this TradingStrategyVersion version) =>
        version == TradingStrategyVersion.AdaptiveSidewaysV6;
}

/// <summary>Playbook đã được nhận diện trước khi lập kế hoạch thực thi.</summary>
public enum SetupType
{
    None = 0,
    LegacyV2 = 1,
    TrendPullback = 2,
    RangeRejection = 3,
    StrongTrendBreakout = 4,
    RectangleRangeFade = 5,
    RectangleBreakout = 6,
    TriangleBreakout = 7,

    /// <summary>
    /// Xu hướng xác định bằng MA7/MA25, vào khi giá hồi về chạm MA7.
    /// </summary>
    /// <remarks>
    /// Khác <see cref="TrendPullback"/> ở chỗ nó đọc xu hướng từ chồng MA chứ không từ chuỗi
    /// phá cấu trúc (BOS) — nên nó vào được cả những nhịp mà bộ dò BOS bỏ qua. Trong 8 ngày đầu
    /// chạy thật, <c>TrendPullback</c> kích hoạt 0 lần còn giá chạm MA7 thuận xu hướng xảy ra
    /// 142–160 lần mỗi mã.
    /// </remarks>
    MaPullback = 8,

    /// <summary>
    /// Vào ngay khi MA7 cắt MA25 trên khung 5m kèm khối lượng mạnh.
    /// </summary>
    /// <remarks>
    /// Nhịp sớm nhất và rủi ro nhất của cùng một xu hướng: chưa có nhịp hồi nào xác nhận, chỉ có
    /// cú đẩy. Đổi lại nó bắt được đoạn di chuyển mà bốn nhánh còn lại đã bỏ lỡ.
    /// </remarks>
    MaCrossFast = 9,

    /// <summary>
    /// Sau khi giá bị từ chối rõ ở kháng cự, chờ nó hồi sâu về vùng MA99 rồi vào.
    /// </summary>
    /// <remarks>
    /// Nhịp cuối của một xu hướng đang chín: cú từ chối ở đỉnh báo hiệu sắp đi ngang, nên mục
    /// tiêu đặt về lại đúng đỉnh đó chứ không đòi bội R cao. Dừng lỗ dưới MA99 — mức mà nếu thủng
    /// thì cấu trúc xu hướng đã hỏng chứ không còn là một nhịp hồi.
    /// </remarks>
    MaDeepPullback = 10,
}

/// <summary>
/// Funnel setup theo sự kiện. Tách khỏi <see cref="SetupTriggerState"/> vì stage nói một cơ hội
/// đã đi xa tới đâu, còn state nói lý do cụ thể khiến lần quét hiện tại dừng lại.
/// </summary>
public enum SetupFunnelStage
{
    NotEligible = 0,
    EligibleContext = 1,
    StructureCandidate = 2,
    TriggerStarted = 3,
    Confirmed = 4,
}

/// <summary>Trạng thái trigger dùng cho audit và telemetry; không dùng chuỗi tự do.</summary>
public enum SetupTriggerState
{
    NotEvaluated = 0,
    LegacyAccepted = 1,
    NoBreakOfStructure = 2,
    BreakUnretested = 3,
    RetestFailed = 4,
    RetestStale = 5,
    ImpulseWeak = 6,
    PullbackVolumeExpanded = 7,
    ReclaimWeak = 8,
    RangeNotSwept = 9,
    RangeRejectionWeak = 10,
    Confirmed = 11,
    CostRejected = 12,
    RangeGeometryWeak = 13,
    RangeConfirmationMissing = 14,
    CompressionMissing = 15,
    BreakoutMissing = 16,
    BreakoutWeak = 17,
    BreakoutRetestMissing = 18,
    StrategyFiltered = 19,

    /// <summary>Chồng MA chưa xếp thuận chiều đang xét.</summary>
    MaTrendMissing = 20,

    /// <summary>MA đã xếp thuận nhưng cú đẩy tạo ra xu hướng không đủ khối lượng.</summary>
    MaImpulseWeak = 21,

    /// <summary>Xu hướng thuận, đủ lực, nhưng giá chưa hồi về chạm MA nhanh.</summary>
    MaPullbackMissing = 22,

    /// <summary>Đã quá lâu kể từ lúc MA cắt nhau — nhịp này không còn là nhịp vừa sinh ra.</summary>
    MaPullbackStale = 23,

    /// <summary>Chưa có cú từ chối rõ ở kháng cự/hỗ trợ để mở nhịp hồi sâu.</summary>
    MaRejectionMissing = 24,

    /// <summary>Đã có cú từ chối nhưng giá chưa hồi về vùng MA chậm nhất.</summary>
    MaDeepZoneMissing = 25,
}

/// <summary>
/// Mức tác động của một sự kiện vĩ mô. Đặt ở Domain vì thực thể <c>ScheduledEvent</c> dùng nó;
/// trước đây nằm ở <c>MMW.Application.Models</c>, nơi Domain không với tới được.
/// </summary>
public enum MacroEventImpact
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Trạng thái thị trường của một ngày, do tầng kế hoạch ngày phân loại.</summary>
public enum DayRegime
{
    TrendUp = 1,
    TrendDown = 2,
    Range = 3,
    HighVolatility = 4,
    EventDay = 5,
}

/// <summary>Vùng biến động theo percentile 90 ngày của ATR(14) D1 chia giá.</summary>
public enum VolatilityRegime
{
    /// <summary>Percentile &lt; 25.</summary>
    Low = 1,
    /// <summary>Percentile 25–75.</summary>
    Normal = 2,
    /// <summary>Percentile 75–90.</summary>
    High = 3,
    /// <summary>Percentile &gt; 90.</summary>
    Extreme = 4,
}

/// <summary>Chiều được phép vào lệnh trong ngày. Ngày trend chỉ cho một chiều thuận trend.</summary>
public enum AllowedDirections
{
    None = 0,
    LongOnly = 1,
    ShortOnly = 2,
    Both = 3,
}

/// <summary>Loại sự kiện trên cuốn lịch nội bộ.</summary>
public enum ScheduledEventKind
{
    Cpi = 1,
    Ppi = 2,
    Nfp = 3,
    FomcStatement = 4,
    FomcPressConference = 5,
    Pce = 6,
    Gdp = 7,
    JoblessClaims = 8,

    /// <summary>Đáo hạn quyền chọn Deribit, thứ Sáu 08:00 UTC.</summary>
    OptionsExpiry = 20,
    /// <summary>Thanh toán phí vốn, 00:00/08:00/16:00 UTC.</summary>
    FundingSettlement = 21,
    /// <summary>Khoảng trống CME cuối tuần.</summary>
    WeekendGap = 22,

    /// <summary>Tin sốc đột xuất do lớp bối cảnh AI chấm severity cao.</summary>
    AiDetectedShock = 90,
}

/// <summary>Nguồn gốc một sự kiện: nạp tay, sinh bằng công thức, hay do AI phát hiện.</summary>
public enum ScheduledEventOrigin
{
    /// <summary>Nạp tay từ lịch công bố của BLS/Fed.</summary>
    Seeded = 1,
    /// <summary>Sinh bằng công thức lịch, không cần nguồn ngoài.</summary>
    Derived = 2,
    /// <summary>Do lớp bối cảnh AI phát hiện. KHÔNG BAO GIỜ dùng cho sự kiện có ngày giờ cố định.</summary>
    AiDetected = 3,
}

/// <summary>Nhóm tiêu chí chấm điểm.</summary>
public enum ScoreGroup
{
    Technical = 1,
    Market = 2,
    Liquidity = 3,
    /// <summary>Nhóm kỷ luật CHỈ TRỪ điểm, không bao giờ cộng.</summary>
    Discipline = 4,
}

/// <summary>Kết cục của một phiếu chấm điểm.</summary>
public enum ScorecardOutcome
{
    Entered = 1,

    /// <summary>Điểm KHÔNG đủ ngưỡng vào lệnh.</summary>
    BelowThreshold = 2,
    Vetoed = 3,

    /// <summary>
    /// Điểm ĐỦ ngưỡng nhưng không setup nào xác nhận: cỡ lệnh bằng 0 vì hệ số chất lượng setup
    /// bằng 0, không phải vì điểm thấp.
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="BelowThreshold"/> vì hai thứ này đòi hai phản ứng khác hẳn nhau.
    /// Điểm thấp nghĩa là bối cảnh xấu — đúng thiết kế, không có gì phải sửa. Setup vắng nghĩa là
    /// bối cảnh TỐT mà bộ kích hoạt không bắt được kèo; nếu con số này lớn thì chính bộ kích hoạt
    /// là thứ cần xem lại. Gộp chung một nhãn thì câu hỏi đó không bao giờ đặt ra được.
    /// </remarks>
    SetupMissing = 4,
}

/// <summary>
/// Kết cục THỰC TẾ của một phiếu khi chạy tiếp trên giá, bất kể phiếu đó có được vào lệnh hay không.
/// </summary>
/// <remarks>
/// Đây là thước đo các CỔNG, không phải thước đo lệnh. Một phiếu bị veto rồi giá đi ngược nghĩa
/// là cổng đã cứu; bị veto mà giá chạm mục tiêu nghĩa là cổng chặn nhầm. Không có bản ghi này thì
/// không câu nào trong hai câu đó trả lời được.
///
/// <see cref="OpenAtHorizon"/> KHÔNG phải hoà: nó nói giá không chạm mức nào trong cửa sổ đo, và
/// đó là một kết quả riêng biệt cần đếm riêng. Thiếu nến thì không sinh bản ghi — không được lẫn
/// "chưa đo được" vào "đã đo và không chạm gì".
/// </remarks>
public enum ScorecardReviewOutcome
{
    Target = 1,
    Stop = 2,
    TimeStop = 3,
    OpenAtHorizon = 4,
}

/// <summary>Loại bản ghi bối cảnh do AI sinh.</summary>
public enum MarketContextKind
{
    DailyBrief = 1,
    NewsItem = 2,
}

/// <summary>
/// Lý do từ chối vào lệnh. Là enum chứ không phải chuỗi tự do vì nó sẽ được đếm và xếp hạng:
/// "3 tháng qua lý do từ chối phổ biến nhất là gì" là câu hỏi trader sẽ hỏi.
/// </summary>
public enum VetoReason
{
    NoDailyPlan = 300,
    DirectionNotAllowed = 301,
    HtfMisaligned = 302,
    InBlackoutWindow = 303,
    LossStreakStop = 304,
    DailyLossStop = 305,
    RevengeWindow = 306,
    Oversized = 307,
    MaxTradesReached = 308,
    InsufficientData = 309,
    DuplicateCandle = 310,

    /// <summary>Đã có vị thế mở trên đúng mã này.</summary>
    PositionAlreadyOpen = 311,

    /// <summary>Đã chạm trần số vị thế mở đồng thời của tài khoản.</summary>
    ConcurrentPositionLimit = 312,

    /// <summary>Khoảng cách tới mục tiêu cấu trúc không đủ trả chi phí một vòng lệnh.</summary>
    InsufficientRoom = 313,

    /// <summary>
    /// Ngày đi ngang, nhưng giá không nằm ở vùng biên — giữa biên độ, hoặc đã ra hẳn ngoài nó.
    /// </summary>
    NotAtRangeEdge = 314,

    /// <summary>
    /// Lý do lịch sử của V2 bước 3. Gate đã bị loại sau A/B #23/#24; giữ enum để đọc phiếu cũ.
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="BelowThreshold"/> của <c>ScorecardOutcome</c> có chủ ý: "cả hai chiều
    /// đều yếu" và "hai chiều mạnh ngang nhau" là hai câu chuyện khác nhau, và câu hỏi "ba tháng
    /// qua vì sao hệ thống đứng ngoài" chỉ trả lời được nếu chúng không bị gộp.
    /// </remarks>
    DirectionUnclear = 315,

    /// <summary>V3: bối cảnh có thể tốt nhưng event trigger bắt buộc của setup chưa hoàn tất.</summary>
    SetupTriggerMissing = 316,

    /// <summary>V3: kế hoạch có gross R:R nhưng lợi thế ròng không đủ trả execution cost.</summary>
    ExecutionCostTooHigh = 317,

    /// <summary>V5+: trigger hợp lệ nhưng không qua admission đã đóng băng của strategy version.</summary>
    StrategyAdmissionRejected = 318,
}
