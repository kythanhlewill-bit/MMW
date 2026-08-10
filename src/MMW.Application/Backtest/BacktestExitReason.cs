namespace MMW.Application.Backtest;

/// <summary>Lý do vị thế mô phỏng đóng hoàn toàn.</summary>
public enum BacktestExitReason
{
    Target = 1,
    Stop = 2,
    TimeStop = 3,
    EndOfPeriod = 4,
}
