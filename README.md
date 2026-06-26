SAILOR V21-V24 conduct update

Extract this ZIP over the repository root:
D:\Site\sailor

Main changes:
- Reworked v21-15minutes, v22-15minutes, v23-5minutes, and v24-5minutes.
- They now use a Sailor-native EMA9 angle conduct layer.
- V21/V22 use 15-minute candles; V23/V24 use 5-minute candles.
- EMA9 angle threshold is +/-12 degrees.
- Entries/exits are evaluated on each incoming 1m backtest bar as the backtest proxy for live/paper continuous checking.
- Long entry: EMA9 angle >= +12 degrees and green candle crosses/holds above EMA9.
- Long flatten: angle falls below +12 degrees, or a red candle crosses EMA9 / breaks last green support.
- Short entry: EMA9 angle <= -12 degrees and red candle crosses/holds below EMA9.
- Short flatten: angle rises above -12 degrees, or a green candle crosses EMA9 / breaks last red resistance.
- Root logs remain under logs\Backtest.
- Harvester legacy code remains excluded; only conduct behavior is reproduced Sailor-native.
