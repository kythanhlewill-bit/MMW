using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Thân Hangfire job: quét watchlist → lấy nến → phân tích →
/// (1) append lịch sử chỉ số, (2) upsert snapshot mới nhất, (3) sinh & lưu đề xuất lệnh.
/// </summary>
public class MarketScanService : IMarketScanService
{
    /// <summary>Số nến lấy mỗi lần (đủ cho EMA50/MACD).</summary>
    private const int CandleLimit = 200;

    private readonly IBaseRepository<WatchItem> _watchItems;
    private readonly IBaseRepository<MarketSnapshot> _snapshots;
    private readonly IBaseRepository<IndicatorRecord> _history;
    private readonly IBaseRepository<TradeSignal> _signals;
    private readonly IMarketDataProvider _marketData;
    private readonly IMarketAnalyzer _analyzer;
    private readonly ISignalGenerator _signalGenerator;
    private readonly ISettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;

    public MarketScanService(
        IBaseRepository<WatchItem> watchItems,
        IBaseRepository<MarketSnapshot> snapshots,
        IBaseRepository<IndicatorRecord> history,
        IBaseRepository<TradeSignal> signals,
        IMarketDataProvider marketData,
        IMarketAnalyzer analyzer,
        ISignalGenerator signalGenerator,
        ISettingsService settings,
        IUnitOfWork unitOfWork)
    {
        _watchItems = watchItems;
        _snapshots = snapshots;
        _history = history;
        _signals = signals;
        _marketData = marketData;
        _analyzer = analyzer;
        _signalGenerator = signalGenerator;
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    public async Task<ScanResult> ScanAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _watchItems.FindListAsync(w => w.IsActive);
        var minScore = (await _settings.GetAppSettingAsync(cancellationToken)).MinSignalScore;
        var scanned = 0;
        var failed = 0;

        foreach (var item in items)
        {
            try
            {
                var candles = await _marketData.GetCandlesAsync(item.Symbol, item.Interval, CandleLimit, cancellationToken);
                if (candles.Count == 0)
                {
                    failed++;
                    continue;
                }

                var now = DateTime.UtcNow;
                var a = _analyzer.Analyze(candles);

                await _history.AddAsync(BuildHistory(item, a, now));
                await UpsertSnapshotAsync(item, a, now);

                var signal = _signalGenerator.Generate(a, minScore);
                if (signal is not null)
                    await _signals.AddAsync(BuildSignal(item, signal, now));

                await _unitOfWork.CommitAsync(cancellationToken);
                scanned++;
            }
            catch
            {
                // Một symbol lỗi (mạng/symbol sai) không chặn các symbol khác.
                failed++;
            }
        }

        return new ScanResult(scanned, failed);
    }

    private static IndicatorRecord BuildHistory(WatchItem item, MarketAnalysis a, DateTime now) => new()
    {
        Symbol = item.Symbol,
        Interval = item.Interval,
        Price = a.Price,
        Rsi = a.Rsi,
        Ema20 = a.Ema20,
        Ema50 = a.Ema50,
        Macd = a.Macd,
        MacdSignal = a.MacdSignal,
        MacdHistogram = a.MacdHistogram,
        Atr = a.Atr,
        Bias = a.Bias,
        ScannedAt = now,
    };

    private static TradeSignal BuildSignal(WatchItem item, SuggestedSignal s, DateTime now) => new()
    {
        Symbol = item.Symbol,
        Interval = item.Interval,
        Direction = s.Direction,
        Bias = s.Bias,
        Score = s.Score,
        Entry = s.Entry,
        StopLoss = s.StopLoss,
        TakeProfit = s.TakeProfit,
        RiskReward = s.RiskReward,
        Reason = s.Reason,
        CreatedAt = now,
    };

    private async Task UpsertSnapshotAsync(WatchItem item, MarketAnalysis a, DateTime now)
    {
        var list = await _snapshots.FindListAsync(s => s.Symbol == item.Symbol && s.Interval == item.Interval);
        var snapshot = list.Count > 0 ? await _snapshots.FindAsync(list[0].Id) : null;

        var isNew = snapshot is null;
        snapshot ??= new MarketSnapshot { Symbol = item.Symbol, Interval = item.Interval };

        snapshot.Price = a.Price;
        snapshot.Rsi = a.Rsi;
        snapshot.Ema20 = a.Ema20;
        snapshot.Ema50 = a.Ema50;
        snapshot.Macd = a.Macd;
        snapshot.MacdSignal = a.MacdSignal;
        snapshot.MacdHistogram = a.MacdHistogram;
        snapshot.Atr = a.Atr;
        snapshot.Bias = a.Bias;
        snapshot.Notes = a.Notes;
        snapshot.UpdatedAt = now;

        if (isNew)
            await _snapshots.AddAsync(snapshot);
        else
            _snapshots.Update(snapshot);
    }
}
