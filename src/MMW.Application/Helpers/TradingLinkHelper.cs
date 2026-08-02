using MMW.Domain.Entities;

namespace MMW.Application.Helpers;

public static class TradingLinkHelper
{
    public static string BuildBinanceUsdmFuturesUrl(string symbol)
    {
        var clean = new string(symbol.Trim().ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray());

        return $"https://www.binance.com/en/futures/{clean}";
    }

    public static string BuildMmwCreateTradePath(TradeSignal signal) =>
        $"/Trades/Create?signalId={signal.Id}";
}
