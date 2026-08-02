# Contract — Chấm điểm vào lệnh

**Namespace**: `MMW.Application.Trading.Scoring` · `MMW.Application.Trading.Discipline` · `MMW.Application.Trading.Sizing`

Hợp đồng nội bộ. Điểm mấu chốt: **vòng tổng hợp không được biết tiêu chí cụ thể nào tồn tại** (Nguyên tắc V).

---

## `IScoreCriterion` — hợp đồng plug-in

```csharp
public interface IScoreCriterion
{
    /// <summary>Định danh ổn định, ví dụ "technical.htf_alignment". KHÔNG được đổi sau khi đã có dữ liệu lịch sử.</summary>
    string Key { get; }

    ScoreGroup Group { get; }

    /// <summary>Điểm tối đa tiêu chí này đóng góp. Tổng theo nhóm phải khớp trọng số trong EngineSetting.</summary>
    int MaxPoints { get; }

    /// <summary>Tính điểm. PHẢI thuần: cùng context → cùng kết quả. KHÔNG được gọi mạng, KHÔNG được đọc đồng hồ.</summary>
    CriterionResult Evaluate(ScoringContext context);
}
```

```csharp
public sealed record CriterionResult(
    int AwardedPoints,
    string Reason,             // tiếng Việt, PHẢI nêu số liệu thực tế so với ngưỡng
    bool DataAvailable = true,
    bool IsHardVeto = false,
    VetoReason? VetoReason = null,
    bool IsApproximation = false);
```

### Ràng buộc bắt buộc

| # | Ràng buộc | Cưỡng chế bằng |
|---|---|---|
| 1 | `0 ≤ AwardedPoints ≤ MaxPoints` với nhóm Technical/Market/Liquidity | Test cho từng tiêu chí |
| 2 | `AwardedPoints ≤ 0` với nhóm Discipline (chỉ trừ, không cộng) | Test |
| 3 | `DataAvailable == false` ⟹ `AwardedPoints == 0` (FR-006) | Test dùng context thiếu dữ liệu |
| 4 | Không truy cập `DateTime.UtcNow` — mọi thời gian lấy từ `context.EvaluatedAtUtc` | Test reflection quét namespace |
| 5 | Không nhận `ILlmService` trong constructor (FR-041) | Test reflection trên constructor |
| 6 | `Evaluate` không gọi I/O — mọi dữ liệu đã nằm sẵn trong `context` | Review + test không mock mạng |

Ràng buộc 6 là thứ khiến kiểm thử lịch sử chạy nhanh: nạp dữ liệu một lần, chấm điểm hàng chục nghìn lần trong bộ nhớ.

---

## `ScoringContext` — đầu vào bất biến

```csharp
public sealed record ScoringContext
{
    public required string Symbol { get; init; }
    public required DateTime EvaluatedAtUtc { get; init; }
    public required DateTime CandleCloseTimeUtc { get; init; }
    public required TradeDirection Direction { get; init; }

    // Chuỗi nến ĐÃ ĐÓNG, mới nhất ở cuối
    public required IReadOnlyList<Candle> EntryCandles { get; init; }   // 15m
    public required IReadOnlyList<Candle> BiasCandles  { get; init; }   // 4h
    public required IReadOnlyList<Candle> DailyCandles { get; init; }   // 1d

    public required decimal CurrentPrice { get; init; }                 // từ ticker, KHÔNG từ nến hở
    public required DailyPlan DailyPlan { get; init; }
    public required EngineSetting Settings { get; init; }

    // Có thể null khi nguồn không khả dụng → tiêu chí liên quan trả DataAvailable = false
    public FundingSnapshot?     Funding      { get; init; }
    public OpenInterestSeries?  OpenInterest { get; init; }
    public DepthSnapshot?       Depth        { get; init; }
    public LongShortRatio?      LongShort    { get; init; }
    public decimal?             LeaderCorrelation { get; init; }

    public required TraderStatistics TraderStats { get; init; }
    public required IReadOnlyList<MarketContextRecord> ActiveAiContext { get; init; }
}
```

`ScoringContext` là `record` bất biến có chủ đích: một tiêu chí không thể vô tình làm bẩn đầu vào của tiêu chí chạy sau nó.

---

## 13 tiêu chí và phân bổ điểm

| Key | Nhóm | Max | Veto cứng |
|---|---|---|---|
| `technical.htf_alignment` | Technical | 10 | ✅ khi ngược kế hoạch ngày |
| `technical.market_structure` | Technical | 10 | |
| `technical.entry_location` | Technical | 8 | |
| `technical.momentum` | Technical | 7 | |
| `technical.volume_confirmation` | Technical | 5 | |
| `market.day_regime_match` | Market | 10 | ✅ khi ngược trạng thái ngày |
| `market.volatility_regime` | Market | 6 | |
| `market.session_quality` | Market | 6 | |
| `market.leader_correlation` | Market | 4 | |
| `market.funding_crowding` | Market | 4 | |
| `liquidity.open_interest` | Liquidity | 5 | |
| `liquidity.zone_position` | Liquidity | 5 | luôn `IsApproximation = true` (R-010) |
| `liquidity.spread_depth` | Liquidity | 5 | |

**Tổng 85 điểm.** 15 điểm còn lại của thang 100 thuộc nhóm kỷ luật, vốn chỉ trừ — nghĩa là điểm 100 tuyệt đối là không đạt được. Đây là **thiết kế có chủ ý**: không có setup nào hoàn hảo, và thang điểm không nên gợi ý điều ngược lại.

---

## `IEntryScorer` — vòng tổng hợp

```csharp
public interface IEntryScorer
{
    ScoringOutcome Score(ScoringContext context);
}
```

```csharp
public sealed record ScoringOutcome(
    int TotalScore,
    int TechnicalScore,
    int MarketScore,
    int LiquidityScore,
    int DisciplinePenalty,
    bool IsVetoed,
    VetoReason? VetoReason,
    string? VetoDetail,
    IReadOnlyList<CriterionResult> Lines);
```

### Thuật toán (cố định, không đổi khi thêm tiêu chí)

```
1. Duyệt các tiêu chí theo thứ tự Group rồi Key (thứ tự tất định).
2. Gặp IsHardVeto = true → DỪNG NGAY, trả IsVetoed. Các tiêu chí còn lại KHÔNG chạy.
3. Cộng điểm theo nhóm.
4. Cộng DisciplinePenalty (số âm).
5. TotalScore = clamp(tổng, 0, 100).
```

**Dừng sớm ở bước 2 là hành vi bắt buộc, không phải tối ưu hoá**: nó giữ cho phiếu chấm điểm nêu đúng **một** lý do từ chối thay vì một danh sách gây nhiễu. Lý do đầu tiên gặp phải là lý do được ghi.

`EntryScorer` nhận `IEnumerable<IScoreCriterion>` từ DI. Thêm tiêu chí = thêm lớp + một dòng đăng ký. **Sửa `EntryScorer` để thêm tiêu chí là vi phạm Nguyên tắc V.**

---

## `IDisciplineGate` — gate kỷ luật

```csharp
public interface IDisciplineGate
{
    string Key { get; }
    GateResult Evaluate(DisciplineContext context);
}

public sealed record GateResult(
    GateAction Action,           // Allow | ReduceSize | BlockTrade | StopForDay
    decimal SizeMultiplier,      // chỉ dùng khi ReduceSize; PHẢI ≤ 1.0
    int ScorePenalty,            // ≤ 0
    string Reason,               // tiếng Việt, nêu số liệu thực tế so với ngưỡng
    VetoReason? VetoReason);
```

| Key | Điều kiện | Hành động |
|---|---|---|
| `discipline.loss_streak` | 2 thua liên tiếp / 3 thua liên tiếp | `ReduceSize` 0.5 / `StopForDay` |
| `discipline.daily_loss_limit` | Lỗ ngày ≥ `RiskSetting.MaxDailyLossPercent` | `StopForDay` |
| `discipline.revenge_window` | Lệnh thua gần nhất đóng trong `EngineSetting.RevengeBlockMinutes` | `BlockTrade` |
| `discipline.oversized` | Kích thước > `OversizeBlockMultiple` × trung bình `OversizeLookbackTrades` lệnh | `BlockTrade` |
| `discipline.max_trades` | Đã đủ `DailyPlan.MaxTradesToday` | `BlockTrade` |
| `discipline.worst_hours` | Giờ hiện tại ∈ 2 giờ tệ nhất **và** đủ `PersonalStatsMinClosedTrades` | `ScorePenalty = -10` |

**Ràng buộc**: `SizeMultiplier ≤ 1.0` **luôn đúng**. Không gate nào được làm lệnh to lên. Cưỡng chế bằng test trên toàn bộ gate.

`discipline.worst_hours` là gate duy nhất phụ thuộc số lệnh lịch sử; dưới ngưỡng mẫu nó trả `Allow` với `ScorePenalty = 0`, **không** trả điểm thưởng.

---

## `IPositionSizer`

```csharp
public interface IPositionSizer
{
    SizingResult Calculate(ScoringOutcome score, DailyPlan plan, GateAggregate gates,
                           decimal aiMultiplier, EngineSetting settings);
}
```

Công thức (FR-034):

```
baseSizeR = score.TotalScore theo bảng ngưỡng trong EngineSetting
finalSizeR = baseSizeR × plan.RiskMultiplier × gates.SizeMultiplier × aiMultiplier
```

### Bất biến — cưỡng chế bằng test, không chỉ bằng review

```
finalSizeR ≤ baseSizeR                    // ba hệ số nhân đều ≤ 1.0
aiMultiplier ≤ 1.0                        // FR-042
score.TotalScore < MinScoreToEnter  ⟹  finalSizeR == 0
score.IsVetoed                      ⟹  finalSizeR == 0
```

Bất biến thứ nhất là lý do cả ba hệ số được định nghĩa là "nhân với một số ≤ 1" thay vì "cộng/trừ điều chỉnh": nó biến "AI không bao giờ làm lệnh to lên" thành một tính chất số học không thể lách, thay vì một quy ước phải nhớ.
