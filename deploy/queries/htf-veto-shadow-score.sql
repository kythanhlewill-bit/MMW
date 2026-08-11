-- ═══════════════════════════════════════════════════════════════════════════
-- Cổng HTF đang chặn những cơ hội chất lượng ra sao?
--
-- Câu hỏi: trong các phiếu bị veto HtfMisaligned, nếu bỏ cổng đó ra thì chúng
-- chấm được bao nhiêu, và bao nhiêu phần trăm vượt ngưỡng vào lệnh?
--
-- MỐC CẮT BẮT BUỘC: phiếu sinh trước commit 35f6f24 (deploy 2026-08-11
-- 13:46:15 UTC) mang điểm CỤT — vòng chấm thoát ngay ở tiêu chí veto nên
-- Market/Liquidity luôn 0 và AvailableMaxPoints dừng ở 8/85. Gộp chúng vào sẽ
-- kéo điểm bóng xuống gần 0 và cho kết luận ngược. Chặn ngay từ @cutoff.
-- ═══════════════════════════════════════════════════════════════════════════

SET NOCOUNT ON;

DECLARE @cutoff  datetime2(7) = '2026-08-11T13:46:15';  -- deploy build chấm đủ 14 tiêu chí
DECLARE @htf     int          = 302;                    -- VetoReason.HtfMisaligned
DECLARE @vetoed  int          = 3;                      -- ScorecardOutcome.Vetoed

-- Điểm bóng = điểm phiếu ĐÃ CÓ nếu không bị veto. Cộng DisciplinePenalty (luôn ≤ 0)
-- rồi kẹp [0,100], đúng như SignalEvalService dựng TotalScore cho phiếu không veto.
-- So ngưỡng bằng NHÂN CHÉO trên thang đo được, không so tuyệt đối — sao chép
-- ScoringOutcome.Reaches(). Chia số nguyên sẽ cắt cụt; so tuyệt đối làm ngưỡng
-- sai đi mỗi khi một nguồn dữ liệu chết làm AvailableMaxPoints teo lại.
-- Bảng tạm chứ không phải CTE: CTE chỉ sống trong ĐÚNG một câu lệnh, mà bên dưới
-- có sáu lát cắt cùng đọc một tập.
DROP TABLE IF EXISTS #judged;

SELECT
    c.Id, c.Symbol, c.EvaluatedAtUtc, c.SetupStage, c.SetupType, c.TriggerState,
    c.Direction, c.TechnicalScore, c.MarketScore, c.LiquidityScore,
    c.DisciplinePenalty, c.AvailableMaxPoints, c.TotalMaxPoints,
    e.MinScoreToEnter, e.ScoreThresholdFull, e.ScoreThresholdMax,
    ShadowScore = CASE
        WHEN c.TechnicalScore + c.MarketScore + c.LiquidityScore + c.DisciplinePenalty < 0 THEN 0
        WHEN c.TechnicalScore + c.MarketScore + c.LiquidityScore + c.DisciplinePenalty > 100 THEN 100
        ELSE c.TechnicalScore + c.MarketScore + c.LiquidityScore + c.DisciplinePenalty
    END
INTO #shadow
FROM EntryScorecards c
JOIN EngineSettings e ON e.TradingAccountId = c.TradingAccountId
WHERE c.Outcome = @vetoed
  AND c.VetoReason = @htf
  AND c.EvaluatedAtUtc >= @cutoff;

SELECT *,
    ReachesEntry = CASE WHEN CAST(ShadowScore AS bigint) * TotalMaxPoints
                           >= CAST(MinScoreToEnter AS bigint) * AvailableMaxPoints
                        THEN 1 ELSE 0 END,
    ReachesFull  = CASE WHEN CAST(ShadowScore AS bigint) * TotalMaxPoints
                           >= CAST(ScoreThresholdFull AS bigint) * AvailableMaxPoints
                        THEN 1 ELSE 0 END,
    ReachesMax   = CASE WHEN CAST(ShadowScore AS bigint) * TotalMaxPoints
                           >= CAST(ScoreThresholdMax AS bigint) * AvailableMaxPoints
                        THEN 1 ELSE 0 END,
    -- Chỉ stage ≥ TriggerStarted mới là cơ hội THẬT bị bỏ lỡ. Điểm cao ở
    -- EligibleContext chỉ nói bối cảnh đẹp, chưa có gì để vào.
    IsRealMiss   = CASE WHEN SetupStage >= 3 THEN 1 ELSE 0 END
INTO #judged
FROM #shadow;

DROP TABLE #shadow;

-- ── 0. Nền mẫu: luôn in ra phần bị loại, để không đọc nhầm "chưa có gì" ──────
SELECT
    N'0. NỀN MẪU' AS section,
    (SELECT COUNT(*) FROM EntryScorecards
      WHERE Outcome = @vetoed AND VetoReason = @htf AND EvaluatedAtUtc <  @cutoff) AS cut_diem_cut,
    (SELECT COUNT(*) FROM EntryScorecards
      WHERE Outcome = @vetoed AND VetoReason = @htf AND EvaluatedAtUtc >= @cutoff) AS dung_duoc,
    (SELECT CONVERT(varchar(19), MIN(EvaluatedAtUtc), 121) FROM #judged)             AS tu_luc,
    (SELECT CONVERT(varchar(19), MAX(EvaluatedAtUtc), 121) FROM #judged)             AS den_luc;

-- ── 1. Tổng thể: cổng HTF chặn nhầm bao nhiêu phần trăm? ────────────────────
SELECT
    N'1. TỔNG THỂ' AS section,
    COUNT(*)                                        AS n,
    MIN(ShadowScore)                                AS diem_min,
    CAST(AVG(CAST(ShadowScore AS decimal(9,2))) AS decimal(6,1)) AS diem_tb,
    MAX(ShadowScore)                                AS diem_max,
    SUM(ReachesEntry)                               AS vuot_nguong_vao,
    CAST(100.0 * SUM(ReachesEntry) / NULLIF(COUNT(*),0) AS decimal(5,1)) AS pct_vuot,
    SUM(ReachesFull)                                AS dat_bac_full,
    SUM(ReachesMax)                                 AS dat_bac_max,
    SUM(IsRealMiss)                                 AS co_trigger,
    SUM(CASE WHEN ReachesEntry = 1 AND IsRealMiss = 1 THEN 1 ELSE 0 END) AS bo_lo_that
FROM #judged;

-- ── 2. Theo mã ──────────────────────────────────────────────────────────────
SELECT
    N'2. THEO MÃ' AS section, Symbol,
    COUNT(*) AS n,
    CAST(AVG(CAST(ShadowScore AS decimal(9,2))) AS decimal(6,1)) AS diem_tb,
    MAX(ShadowScore) AS diem_max,
    SUM(ReachesEntry) AS vuot_nguong,
    SUM(CASE WHEN ReachesEntry = 1 AND IsRealMiss = 1 THEN 1 ELSE 0 END) AS bo_lo_that
FROM #judged GROUP BY Symbol ORDER BY Symbol;

-- ── 3. Theo giai đoạn setup — điểm cao ở stage thấp KHÔNG phải cơ hội mất ────
SELECT
    N'3. THEO STAGE' AS section,
    SetupStage,
    CASE SetupStage WHEN 0 THEN N'NotEligible' WHEN 1 THEN N'EligibleContext'
                    WHEN 2 THEN N'StructureCandidate' WHEN 3 THEN N'TriggerStarted'
                    WHEN 4 THEN N'Confirmed' ELSE N'?' END AS stage_ten,
    COUNT(*) AS n,
    CAST(AVG(CAST(ShadowScore AS decimal(9,2))) AS decimal(6,1)) AS diem_tb,
    SUM(ReachesEntry) AS vuot_nguong
FROM #judged GROUP BY SetupStage ORDER BY SetupStage;

-- ── 4. Theo ngày — thế kẹt HTF kéo dài hay chỉ một hôm? ─────────────────────
SELECT
    N'4. THEO NGÀY' AS section,
    CAST(EvaluatedAtUtc AS date) AS ngay_utc,
    COUNT(*) AS n,
    CAST(AVG(CAST(ShadowScore AS decimal(9,2))) AS decimal(6,1)) AS diem_tb,
    SUM(ReachesEntry) AS vuot_nguong,
    SUM(CASE WHEN ReachesEntry = 1 AND IsRealMiss = 1 THEN 1 ELSE 0 END) AS bo_lo_that
FROM #judged GROUP BY CAST(EvaluatedAtUtc AS date) ORDER BY ngay_utc;

-- ── 5. 20 phiếu nặng ký nhất — để soi tay từng cái ──────────────────────────
SELECT TOP 20
    N'5. CHI TIẾT' AS section,
    CONVERT(varchar(19), EvaluatedAtUtc, 121) AS t,
    Symbol,
    CASE Direction WHEN 1 THEN N'Long' WHEN 2 THEN N'Short' ELSE N'-' END AS huong,
    ShadowScore AS diem_bong,
    MinScoreToEnter AS nguong,
    CONCAT(TechnicalScore, N'+', MarketScore, N'+', LiquidityScore) AS thanh_phan,
    DisciplinePenalty AS phat,
    CONCAT(AvailableMaxPoints, N'/', TotalMaxPoints) AS do_phu,
    SetupStage AS stage,
    ReachesEntry AS vuot,
    Id
FROM #judged
WHERE IsRealMiss = 1
ORDER BY ShadowScore DESC, EvaluatedAtUtc DESC;
