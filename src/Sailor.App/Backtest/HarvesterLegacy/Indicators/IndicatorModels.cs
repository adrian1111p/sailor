namespace Sailor.App.Backtest.Indicators;

/// <summary>Multi-column MACD result per bar.</summary>
public readonly record struct MacdResult(double Macd, double Signal, double Histogram);

/// <summary>Multi-column Bollinger Bands result per bar.</summary>
public readonly record struct BollingerResult(double Mid, double Upper, double Lower, double PctB, double Bandwidth);

/// <summary>Multi-column ADX result per bar.</summary>
public readonly record struct AdxResult(double Adx, double PlusDi, double MinusDi);

/// <summary>Multi-column Supertrend result per bar.</summary>
public readonly record struct SupertrendResult(double Value, int Direction);

/// <summary>Multi-column Stochastic result per bar.</summary>
public readonly record struct StochasticResult(double K, double D);

/// <summary>Multi-column Keltner Channels result per bar.</summary>
public readonly record struct KeltnerResult(double Mid, double Upper, double Lower);

/// <summary>Multi-column Donchian Channels result per bar.</summary>
public readonly record struct DonchianResult(double Upper, double Lower, double Mid, double Pct);

/// <summary>Multi-column Order Flow Imbalance result per bar.</summary>
public readonly record struct OrderFlowResult(double Raw, double Cumulative, double Signal);

/// <summary>Multi-column Spread proxy result per bar.</summary>
public readonly record struct SpreadResult(double Ratio, double ZScore);

