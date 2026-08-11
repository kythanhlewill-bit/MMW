-- ═══════════════════════════════════════════════════════════════════════════
-- Cổng HTF chặn ĐÚNG hay SAI?
--
-- htf-veto-shadow-score.sql đo chất lượng cơ hội theo đánh giá của chính
-- engine. Truy vấn này đo thứ khác hẳn: giá SAU ĐÓ đi về đâu. Một setup 58
-- điểm bị chặn rồi giá đi ngược nghĩa là cổng HTF đã CỨU, chứ không phải chặn
-- nhầm — và chỉ đọc điểm bóng thì không bao giờ thấy điều đó.
--
-- Cách mô phỏng: lấy Entry/StopLoss/TakeProfit mà engine đã ghi sẵn trên phiếu
-- bị veto, rồi chạy tiến trên kho nến 15m xem chạm stop hay chạm mục tiêu
-- trước.
--
-- BA QUY ƯỚC, sao chép từ SimulatedTradePosition để con số so sánh được với
-- kiểm thử lịch sử thay vì là một thước đo thứ hai tự chế:
--
--   1. CÙNG MỘT NẾN chạm cả hai  ⟹  tính STOP. OHLC không cho biết cái nào
--      đến trước; chọn phía bất lợi. Xem SimulatedTradePosition.cs:323 —
--      hitStop được kiểm TRƯỚC hitTarget.
--   2. Chỉ tính từ nến MỞ SAU thời điểm quyết định. Nến đang chạy dở lúc chấm
--      điểm bị loại, nếu không là nhìn trộm tương lai.
--   3. Chưa chạm gì trong horizon ⟹ 'Open', KHÔNG phải hoà. Và phân biệt
--      'Open' với 'ThiếuNến' — hết dữ liệu khác hẳn với đi ngang.
--
-- CHI PHÍ: cột grossR là R THÔ, chưa trừ phí. Ở khoảng stop hẹp (~0,23% như
-- các phiếu ngày 2026-08-11) phí + trượt giá ăn khoảng 0,6R mỗi vòng, nên
-- grossR nói dối rất nặng. Cột netR áp đúng công thức của
-- ExecutionViabilityPolicy: phí tính theo KHỐI LƯỢNG, mà khối lượng =
-- rủi ro / khoảng stop ⟹ stop càng hẹp, phí theo R càng lớn.
--
-- KHO NẾN TÍCH LUỸ, KHÔNG XOÁ. Không có đường xoá KlineArchives nào trong
-- source; job archive backfill 2 ngày mỗi giờ chỉ để VÁ chỗ thủng, nến cũ vẫn
-- nằm nguyên. Nếu kho trông ngắn thì đó là vì job mới chạy lần đầu (khoảng
-- 2026-08-10) chứ không phải bị dọn — phiếu cũ vẫn mô phỏng lại được sau này.
-- ═══════════════════════════════════════════════════════════════════════════

SET NOCOUNT ON;

DECLARE @cutoff      datetime2(7) = '2026-08-11T13:46:15';  -- xem README, mốc điểm đủ
DECLARE @htf         int          = 302;   -- VetoReason.HtfMisaligned
DECLARE @vetoed      int          = 3;     -- ScorecardOutcome.Vetoed
DECLARE @horizonBars int          = 96;    -- 96 × 15m = 24 giờ
DECLARE @bar         int          = 15;    -- phút mỗi nến

DROP TABLE IF EXISTS #sim;

SELECT
    c.Id, c.Symbol, c.EvaluatedAtUtc, c.SetupStage, c.Direction,
    c.TechnicalScore, c.MarketScore, c.LiquidityScore, c.DisciplinePenalty,
    c.SuggestedEntry AS Entry, c.SuggestedStopLoss AS Stop,
    -- Mục tiêu đầu là mức thoát thực tế; runner chỉ có ý nghĩa khi đã chốt phần đầu.
    COALESCE(c.SuggestedFirstTakeProfit, c.SuggestedTakeProfit) AS Target,
    e.MinScoreToEnter,
    e.BacktestTakerFeePercent  AS TakerPct,
    e.BacktestMakerFeePercent  AS MakerPct,
    e.BacktestEntrySlippageBps AS EntrySlipBps,
    e.BacktestStopSlippageBps  AS StopSlipBps,
    RiskDist = ABS(c.SuggestedEntry - c.SuggestedStopLoss),
    HorizonEnd = DATEADD(minute, @bar * @horizonBars, c.EvaluatedAtUtc)
INTO #sim
FROM EntryScorecards c
JOIN EngineSettings e ON e.TradingAccountId = c.TradingAccountId
WHERE c.Outcome = @vetoed
  AND c.VetoReason = @htf
  AND c.EvaluatedAtUtc >= @cutoff
  AND c.SuggestedEntry IS NOT NULL
  AND c.SuggestedStopLoss IS NOT NULL
  AND COALESCE(c.SuggestedFirstTakeProfit, c.SuggestedTakeProfit) IS NOT NULL
  AND ABS(c.SuggestedEntry - c.SuggestedStopLoss) > 0;

DROP TABLE IF EXISTS #resolved;

SELECT
    s.*,
    -- Nến ĐẦU TIÊN chạm bất kỳ mức nào. Trong cùng nến đó, stop thắng.
    FirstHitAt   = hit.OpenTimeUtc,
    HitKind      = hit.Kind,
    -- Kho nến có phủ hết horizon không? Thiếu nến mà báo 'Open' là nói dối.
    LastBarUtc   = cov.LastBarUtc,
    ShadowScore  = CASE
        WHEN s.TechnicalScore + s.MarketScore + s.LiquidityScore + s.DisciplinePenalty < 0 THEN 0
        WHEN s.TechnicalScore + s.MarketScore + s.LiquidityScore + s.DisciplinePenalty > 100 THEN 100
        ELSE s.TechnicalScore + s.MarketScore + s.LiquidityScore + s.DisciplinePenalty
    END
INTO #resolved
FROM #sim s
OUTER APPLY (
    SELECT TOP 1
        k.OpenTimeUtc,
        Kind = CASE
            WHEN (s.Direction = 1 AND k.Low  <= s.Stop)
              OR (s.Direction = 2 AND k.High >= s.Stop) THEN 'Stop'
            ELSE 'Target'
        END
    FROM KlineArchives k
    WHERE k.Symbol   = s.Symbol
      AND k.Interval = '15m'
      AND k.OpenTimeUtc >= s.EvaluatedAtUtc          -- quy ước 2: không nhìn trộm
      AND k.OpenTimeUtc <  s.HorizonEnd
      AND ( (s.Direction = 1 AND (k.Low  <= s.Stop OR k.High >= s.Target))
         OR (s.Direction = 2 AND (k.High >= s.Stop OR k.Low  <= s.Target)) )
    ORDER BY k.OpenTimeUtc                            -- quy ước 1: nến sớm nhất, stop ưu tiên
) hit
OUTER APPLY (
    SELECT LastBarUtc = MAX(k2.OpenTimeUtc)
    FROM KlineArchives k2
    WHERE k2.Symbol = s.Symbol AND k2.Interval = '15m'
) cov;

DROP TABLE IF EXISTS #final;

SELECT
    r.*,
    Outcome = CASE
        WHEN r.HitKind IS NOT NULL THEN r.HitKind
        -- Chưa chạm gì VÀ kho nến chưa phủ hết horizon ⟹ chưa kết luận được.
        WHEN r.LastBarUtc IS NULL
          OR DATEADD(minute, @bar, r.LastBarUtc) < r.HorizonEnd THEN 'ThieuNen'
        ELSE 'Open'
    END,
    -- R thô: thua đúng 1R, thắng theo tỉ lệ mục tiêu / khoảng stop.
    GrossR = CASE
        WHEN r.HitKind = 'Stop'   THEN -1.0
        WHEN r.HitKind = 'Target' THEN ABS(r.Target - r.Entry) / r.RiskDist
        ELSE NULL
    END,
    StopPct = ABS(r.Entry - r.Stop) / NULLIF(r.Entry, 0) * 100.0
INTO #final
FROM #resolved r;

-- ── 0. NỀN MẪU ──────────────────────────────────────────────────────────────
SELECT N'0. NỀN MẪU' AS section,
    (SELECT COUNT(*) FROM EntryScorecards
      WHERE Outcome = @vetoed AND VetoReason = @htf AND EvaluatedAtUtc < @cutoff) AS cat_diem_cut,
    (SELECT COUNT(*) FROM #final)                                                 AS mo_phong_duoc,
    (SELECT COUNT(*) FROM #final WHERE Outcome = 'ThieuNen')                      AS thieu_nen,
    @horizonBars AS horizon_nen,
    (SELECT CONVERT(varchar(19), MIN(EvaluatedAtUtc), 121) FROM #final)           AS tu_luc,
    (SELECT CONVERT(varchar(19), MAX(EvaluatedAtUtc), 121) FROM #final)           AS den_luc;

-- ── 1. PHÁN QUYẾT: chặn đúng hay sai ────────────────────────────────────────
-- Stop = giá đi ngược = cổng HTF ĐÚNG.  Target = cổng HTF đã chặn nhầm.
SELECT N'1. PHÁN QUYẾT' AS section,
    Outcome,
    COUNT(*) AS n,
    CAST(100.0 * COUNT(*) / NULLIF(SUM(COUNT(*)) OVER (), 0) AS decimal(5,1)) AS pct,
    CAST(AVG(CAST(ShadowScore AS decimal(9,2))) AS decimal(6,1))              AS diem_bong_tb,
    CAST(AVG(GrossR) AS decimal(7,3))                                        AS grossR_tb
FROM #final GROUP BY Outcome ORDER BY n DESC;

-- ── 2. KỲ VỌNG: thô so với sau phí ──────────────────────────────────────────
-- Phí theo R = tỉ lệ phí / khoảng stop (ExecutionViabilityPolicy.cs:57-68).
-- Stop hẹp làm khối lượng phình, mà phí thu trên khối lượng chứ không trên rủi ro.
SELECT N'2. KỲ VỌNG' AS section,
    COUNT(*) AS n_da_dong,
    CAST(AVG(StopPct) AS decimal(6,3))                          AS stop_pct_tb,
    CAST(AVG(GrossR) AS decimal(7,3))                           AS grossR,
    CAST(AVG(CostR) AS decimal(7,3))                            AS chi_phi_R,
    CAST(AVG(GrossR - CostR) AS decimal(7,3))                   AS netR,
    SUM(CASE WHEN GrossR > 0 THEN 1 ELSE 0 END)                 AS thang,
    CAST(100.0 * SUM(CASE WHEN GrossR > 0 THEN 1 ELSE 0 END)
         / NULLIF(COUNT(*),0) AS decimal(5,1))                  AS win_rate
FROM (
    SELECT GrossR, StopPct,
        CostR = CASE WHEN HitKind = 'Target'
            -- vào taker + trượt giá vào + ra maker
            THEN (TakerPct/100.0 + EntrySlipBps/10000.0 + MakerPct/100.0) / (StopPct/100.0)
            -- vào taker + trượt giá vào + ra taker + trượt giá stop
            ELSE (TakerPct/100.0 + EntrySlipBps/10000.0 + TakerPct/100.0 + StopSlipBps/10000.0)
                 / (StopPct/100.0)
        END
    FROM #final WHERE GrossR IS NOT NULL AND StopPct > 0
) x;

-- ── 3. ĐIỂM BÓNG CÓ DỰ BÁO ĐƯỢC KẾT QUẢ KHÔNG? ──────────────────────────────
-- Nếu phiếu điểm cao thắng nhiều hơn phiếu điểm thấp thì bộ tiêu chí có giá trị
-- và cổng HTF đang chặn nhầm. Nếu không khác nhau, điểm chỉ là số trang trí.
SELECT N'3. THEO ĐIỂM' AS section,
    bucket = CASE WHEN ShadowScore >= MinScoreToEnter THEN N'>= nguong'
                  ELSE N'<  nguong' END,
    COUNT(*) AS n,
    SUM(CASE WHEN Outcome = 'Target' THEN 1 ELSE 0 END) AS cham_tp,
    SUM(CASE WHEN Outcome = 'Stop'   THEN 1 ELSE 0 END) AS cham_sl,
    CAST(AVG(GrossR) AS decimal(7,3)) AS grossR_tb
FROM #final WHERE Outcome IN ('Target','Stop')
GROUP BY CASE WHEN ShadowScore >= MinScoreToEnter THEN N'>= nguong' ELSE N'<  nguong' END;

-- ── 4. THEO MÃ VÀ NGÀY ──────────────────────────────────────────────────────
SELECT N'4. THEO MÃ/NGÀY' AS section,
    Symbol, CAST(EvaluatedAtUtc AS date) AS ngay_utc,
    COUNT(*) AS n,
    SUM(CASE WHEN Outcome = 'Target' THEN 1 ELSE 0 END) AS cham_tp,
    SUM(CASE WHEN Outcome = 'Stop'   THEN 1 ELSE 0 END) AS cham_sl,
    SUM(CASE WHEN Outcome = 'Open'   THEN 1 ELSE 0 END) AS con_mo,
    CAST(AVG(GrossR) AS decimal(7,3)) AS grossR_tb
FROM #final GROUP BY Symbol, CAST(EvaluatedAtUtc AS date) ORDER BY ngay_utc, Symbol;

-- ── 5. CHI TIẾT TỪNG PHIẾU ──────────────────────────────────────────────────
SELECT TOP 40 N'5. CHI TIẾT' AS section,
    CONVERT(varchar(19), EvaluatedAtUtc, 121) AS quyet_dinh,
    Symbol,
    CASE Direction WHEN 1 THEN N'Long' WHEN 2 THEN N'Short' ELSE N'-' END AS huong,
    ShadowScore AS diem_bong, MinScoreToEnter AS nguong, SetupStage AS stage,
    CAST(Entry AS decimal(18,2))  AS entry,
    CAST(Stop AS decimal(18,2))   AS sl,
    CAST(Target AS decimal(18,2)) AS tp,
    CAST(StopPct AS decimal(6,3)) AS stop_pct,
    Outcome,
    CONVERT(varchar(19), FirstHitAt, 121) AS cham_luc,
    CAST(GrossR AS decimal(7,3)) AS grossR,
    Id
FROM #final ORDER BY EvaluatedAtUtc DESC, Symbol;

DROP TABLE #sim; DROP TABLE #resolved; DROP TABLE #final;
