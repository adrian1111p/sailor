using Sailor.App.Models;

namespace Sailor.App.Strategies;

public sealed class SimpleMomentumStrategy
{
    private MarketBar? _previousBar;

    public TradeSignal Evaluate(MarketBar currentBar)
    {
        if (_previousBar is null)
        {
            _previousBar = currentBar;
            return TradeSignal.Hold("Waiting for previous bar.");
        }

        decimal previousClose = _previousBar.Close;
        decimal currentClose = currentBar.Close;

        _previousBar = currentBar;

        decimal upThreshold = previousClose * 1.002m;
        decimal downThreshold = previousClose * 0.998m;

        if (currentClose >= upThreshold)
        {
            return TradeSignal.Buy(
                $"Momentum up: close {currentClose} >= threshold {decimal.Round(upThreshold, 2)}");
        }

        if (currentClose <= downThreshold)
        {
            return TradeSignal.Sell(
                $"Momentum down: close {currentClose} <= threshold {decimal.Round(downThreshold, 2)}");
        }

        return TradeSignal.Hold("No clear momentum.");
    }
}
