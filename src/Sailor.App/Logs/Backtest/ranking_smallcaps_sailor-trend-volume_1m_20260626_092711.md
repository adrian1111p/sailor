# Sailor scanner + backtest ranking report

Generated: 2026-06-26 09:27:11
Timeframe: `1m`
Profile: `sailor-trend-volume`
Universe: `smallcaps`
Requested symbols: 67
Symbols with data: 67
Scanner candidates: 5
Backtested candidates: 5

## Profile filters

- Price: 0.50-300.00
- Minimum volume: 100000
- Minimum volume ratio: 1.00
- Entry momentum: 0.20%
- Exit momentum: 0.20%
- Require EMA9 > SMA20: True
- Require close > VWAP: True
- Require close > SMA200 when available: True

## Final ranking by backtest result

| Rank | Symbol | Scanner rank | PnL | Trades | Win rate | Winners | Losers | Scanner score | Momentum | Vol ratio | Close |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | EONR | 1 | 35.23 | 77 | 32.47% | 25 | 46 | 90.03 | 4.79% | 2.05 | 1.53 |
| 2 | MVST | 2 | 11.41 | 2 | 50.00% | 1 | 1 | 49.36 | 2.99% | 3.43 | 2.24 |
| 3 | CLOV | 4 | 8.57 | 5 | 20.00% | 1 | 2 | 32.75 | 0.51% | 3.10 | 1.99 |
| 4 | SNAP | 5 | -7.55 | 17 | 23.53% | 4 | 9 | 32.66 | 0.58% | 2.86 | 5.17 |
| 5 | HIMS | 3 | -25.59 | 55 | 40.00% | 22 | 32 | 46.41 | 1.93% | 3.42 | 24.80 |

## Scanner order before backtest

| Scanner rank | Symbol | Score | Momentum | Vol ratio | Close | Reason |
|---:|---|---:|---:|---:|---:|---|
| 1 | EONR | 90.03 | 4.79% | 2.05 | 1.53 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=2.05 |
| 2 | MVST | 49.36 | 2.99% | 3.43 | 2.24 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.43 |
| 3 | HIMS | 46.41 | 1.93% | 3.42 | 24.80 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.42 |
| 4 | CLOV | 32.75 | 0.51% | 3.10 | 1.99 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.10 |
| 5 | SNAP | 32.66 | 0.58% | 2.86 | 5.17 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=2.86 |

## Backtest log files

- EONR: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_EONR_1m_sailor-trend-volume_20260626_092711.log`
- MVST: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_MVST_1m_sailor-trend-volume_20260626_092711.log`
- CLOV: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CLOV_1m_sailor-trend-volume_20260626_092711.log`
- SNAP: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_SNAP_1m_sailor-trend-volume_20260626_092711.log`
- HIMS: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_HIMS_1m_sailor-trend-volume_20260626_092711.log`

