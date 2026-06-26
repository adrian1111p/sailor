SAILOR-013 modified files

Focus:
- Bring Sailor V21/V22/V23/V24 closer to Harvester backtest mechanics.
- Keep Sailor scanner universe/reporting intact.
- No Harvester live/risk/self-learning dependencies added.

Main corrections:
1. V21/V23/V22/V24 angle engine now uses completed 5m/15m candles for backtest decisions.
2. EMA9 angle is normalized by higher-timeframe ATR, like Harvester StrategyV21/V23, not by percent price slope.
3. The same completed 5m/15m candle no longer emits repeated entry/exit signals each 1m bar.
4. V21/V23/V22/V24 can open SHORT positions when the angle is <= -threshold.
5. Runner can now reserve notional for long or short positions and calculates short PnL correctly.
6. HTML trade table now displays LONG/SHORT side correctly.
7. V21/V23/V24/V22 last entry is aligned to Harvester default 945 ET, with force-flat at 955 ET.
