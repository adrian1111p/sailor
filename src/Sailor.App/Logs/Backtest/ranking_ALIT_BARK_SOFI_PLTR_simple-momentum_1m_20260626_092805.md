# Sailor scanner + backtest ranking report

Generated: 2026-06-26 09:28:05
Timeframe: `1m`
Profile: `simple-momentum`
Universe: `ALIT,BARK,SOFI,PLTR`
Requested symbols: 4
Symbols with data: 4
Scanner candidates: 4
Backtested candidates: 4

## Profile filters

- Price: 0.50-300.00
- Minimum volume: 0
- Minimum volume ratio: 0.00
- Entry momentum: 0.20%
- Exit momentum: 0.20%
- Require EMA9 > SMA20: False
- Require close > VWAP: False
- Require close > SMA200 when available: False

## Final ranking by backtest result

| Rank | Symbol | Scanner rank | PnL | Trades | Win rate | Winners | Losers | Scanner score | Momentum | Vol ratio | Close |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | PLTR | 2 | 16.02 | 36 | 44.44% | 16 | 19 | 21.64 | 0.74% | 0.65 | 145.82 |
| 2 | BARK | 3 | 5.25 | 26 | 38.46% | 10 | 14 | 21.38 | 0.59% | 1.79 | 0.81 |
| 3 | SOFI | 4 | -3.50 | 24 | 33.33% | 8 | 15 | 7.73 | 1.17% | 0.60 | 18.10 |
| 4 | ALIT | 1 | -397.56 | 67 | 11.94% | 8 | 54 | 22.18 | -2.13% | 2.56 | 0.92 |

## Scanner order before backtest

| Scanner rank | Symbol | Score | Momentum | Vol ratio | Close | Reason |
|---:|---|---:|---:|---:|---:|---|
| 1 | ALIT | 22.18 | -2.13% | 2.56 | 0.92 | EMA9<=SMA20, Close>VWAP, Close<=SMA200, VolRatio=2.56 |
| 2 | PLTR | 21.64 | 0.74% | 0.65 | 145.82 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=0.65 |
| 3 | BARK | 21.38 | 0.59% | 1.79 | 0.81 | EMA9>SMA20, Close>VWAP, Close<=SMA200, VolRatio=1.79 |
| 4 | SOFI | 7.73 | 1.17% | 0.60 | 18.10 | EMA9>SMA20, Close<=VWAP, Close>SMA200, VolRatio=0.60 |

## Backtest log files

- PLTR: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_PLTR_1m_simple-momentum_20260626_092805.log`
- BARK: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_BARK_1m_simple-momentum_20260626_092805.log`
- SOFI: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_SOFI_1m_simple-momentum_20260626_092805.log`
- ALIT: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_ALIT_1m_simple-momentum_20260626_092805.log`

