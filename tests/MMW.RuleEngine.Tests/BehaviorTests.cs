using MMW.Application.Behavior;
using MMW.Application.Behavior.Detectors;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class BehaviorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static Trade ClosedLoss(long id, DateTime opened, DateTime closed, decimal pnl = -10m,
        decimal qty = 1m, decimal entry = 100m) => new()
    {
        Id = id,
        Symbol = "BTCUSDT",
        EntryPrice = entry,
        Quantity = qty,
        OpenedAt = opened,
        ClosedAt = closed,
        RealizedPnl = pnl,
    };

    private static Trade ClosedWin(long id, DateTime opened, DateTime closed, decimal pnl = 20m,
        decimal qty = 1m, decimal entry = 100m) => new()
    {
        Id = id,
        Symbol = "BTCUSDT",
        EntryPrice = entry,
        Quantity = qty,
        OpenedAt = opened,
        ClosedAt = closed,
        RealizedPnl = pnl,
    };

    private static BehaviorContext Ctx(Trade trade, IReadOnlyList<Trade> history, RiskSetting? settings = null)
        => new() { Trade = trade, Settings = settings ?? new RiskSetting(), History = history };

    // --- Revenge ---

    [Fact]
    public void Revenge_Flags_When_Entered_Soon_After_Loss()
    {
        var loss = ClosedLoss(1, T0, T0.AddMinutes(10));
        var current = new Trade { Id = 2, Symbol = "X", OpenedAt = T0.AddMinutes(15) }; // 5' sau khi cắt lỗ

        var v = new RevengeTradeDetector().Detect(
            Ctx(current, new[] { loss }, new RiskSetting { RevengeTradeWindowMinutes = 30 }));

        Assert.NotNull(v);
        Assert.Equal(FlagType.RevengeTrade, v!.Type);
    }

    [Fact]
    public void Revenge_Passes_When_Outside_Window()
    {
        var loss = ClosedLoss(1, T0, T0.AddMinutes(10));
        var current = new Trade { Id = 2, Symbol = "X", OpenedAt = T0.AddMinutes(60) }; // 50' sau

        var v = new RevengeTradeDetector().Detect(
            Ctx(current, new[] { loss }, new RiskSetting { RevengeTradeWindowMinutes = 30 }));

        Assert.Null(v);
    }

    [Fact]
    public void Revenge_Passes_When_Previous_Was_Win()
    {
        var win = ClosedWin(1, T0, T0.AddMinutes(10));
        var current = new Trade { Id = 2, Symbol = "X", OpenedAt = T0.AddMinutes(12) };

        var v = new RevengeTradeDetector().Detect(
            Ctx(current, new[] { win }, new RiskSetting { RevengeTradeWindowMinutes = 30 }));

        Assert.Null(v);
    }

    // --- Loss streak ---

    [Fact]
    public void LossStreak_Flags_At_Threshold()
    {
        var history = new[]
        {
            ClosedLoss(1, T0, T0.AddMinutes(5)),
            ClosedLoss(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5)),
            ClosedLoss(3, T0.AddHours(2), T0.AddHours(2).AddMinutes(5)),
        };
        var current = new Trade { Id = 4, Symbol = "X" };

        var v = new LossStreakDetector().Detect(
            Ctx(current, history, new RiskSetting { LossStreakThreshold = 3 }));

        Assert.NotNull(v);
        Assert.Equal(FlagType.LossStreak, v!.Type);
    }

    [Fact]
    public void LossStreak_Resets_After_Win()
    {
        var history = new[]
        {
            ClosedLoss(1, T0, T0.AddMinutes(5)),
            ClosedLoss(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5)),
            ClosedWin(3, T0.AddHours(2), T0.AddHours(2).AddMinutes(5)), // cắt chuỗi
            ClosedLoss(4, T0.AddHours(3), T0.AddHours(3).AddMinutes(5)),
        };
        var current = new Trade { Id = 5, Symbol = "X" };

        var v = new LossStreakDetector().Detect(
            Ctx(current, history, new RiskSetting { LossStreakThreshold = 3 }));

        Assert.Null(v); // chỉ 1 lệnh thua sau lệnh thắng
    }

    // --- Oversized after loss (tilt) ---

    [Fact]
    public void Oversized_Flags_When_Size_Spikes_After_Loss()
    {
        // 3 lệnh ~ size 100 (qty1*entry100), lệnh trước là thua → lệnh hiện tại qty 3 = size 300 (+200%)
        var history = new[]
        {
            ClosedWin(1, T0, T0.AddMinutes(5), qty: 1m, entry: 100m),
            ClosedWin(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5), qty: 1m, entry: 100m),
            ClosedLoss(3, T0.AddHours(2), T0.AddHours(2).AddMinutes(5), qty: 1m, entry: 100m),
        };
        var current = new Trade { Id = 4, Symbol = "X", EntryPrice = 100m, Quantity = 3m };

        var v = new OversizedAfterLossDetector().Detect(
            Ctx(current, history, new RiskSetting { TiltSizeIncreasePercent = 50m }));

        Assert.NotNull(v);
        Assert.Equal(FlagType.OversizedAfterLoss, v!.Type);
    }

    [Fact]
    public void Oversized_Passes_When_Previous_Not_Loss()
    {
        var history = new[]
        {
            ClosedWin(1, T0, T0.AddMinutes(5), qty: 1m, entry: 100m),
            ClosedWin(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5), qty: 1m, entry: 100m),
        };
        var current = new Trade { Id = 3, Symbol = "X", EntryPrice = 100m, Quantity = 5m };

        var v = new OversizedAfterLossDetector().Detect(
            Ctx(current, history, new RiskSetting { TiltSizeIncreasePercent = 50m }));

        Assert.Null(v);
    }

    [Fact]
    public void Oversized_Passes_When_Size_Normal()
    {
        var history = new[]
        {
            ClosedWin(1, T0, T0.AddMinutes(5), qty: 1m, entry: 100m),
            ClosedLoss(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5), qty: 1m, entry: 100m),
        };
        var current = new Trade { Id = 3, Symbol = "X", EntryPrice = 100m, Quantity = 1m }; // size như cũ

        var v = new OversizedAfterLossDetector().Detect(
            Ctx(current, history, new RiskSetting { TiltSizeIncreasePercent = 50m }));

        Assert.Null(v);
    }

    // --- Analyzer aggregation ---

    [Fact]
    public void Analyzer_Aggregates_Revenge_And_Streak()
    {
        var analyzer = new BehaviorAnalyzer(new IBehaviorDetector[]
        {
            new RevengeTradeDetector(),
            new LossStreakDetector(),
            new OversizedAfterLossDetector(),
        });

        var history = new[]
        {
            ClosedLoss(1, T0, T0.AddMinutes(5)),
            ClosedLoss(2, T0.AddHours(1), T0.AddHours(1).AddMinutes(5)),
            ClosedLoss(3, T0.AddHours(2), T0.AddHours(2).AddMinutes(10)),
        };
        // Vào lệnh 5' sau lần thua cuối + đang chuỗi 3 thua
        var current = new Trade { Id = 4, Symbol = "X", EntryPrice = 100m, Quantity = 1m, OpenedAt = T0.AddHours(2).AddMinutes(15) };

        var signals = analyzer.Analyze(
            Ctx(current, history, new RiskSetting { RevengeTradeWindowMinutes = 30, LossStreakThreshold = 3 }));

        Assert.Contains(signals, s => s.Type == FlagType.RevengeTrade);
        Assert.Contains(signals, s => s.Type == FlagType.LossStreak);
    }
}
