# Sailor scanner + backtest ranking report

Generated: 2026-06-26 09:27:37
Timeframe: `1m`
Profile: `simple-momentum`
Universe: `smallcaps`
Requested symbols: 67
Symbols with data: 67
Scanner candidates: 20
Backtested candidates: 20

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
| 1 | IONQ | 8 | 7.03 | 21 | 33.33% | 7 | 14 | 43.31 | 0.05% | 5.04 | 32.97 |
| 2 | LCID | 15 | 5.69 | 31 | 41.94% | 13 | 18 | 35.78 | 0.61% | 3.76 | 9.91 |
| 3 | UPST | 10 | 5.41 | 19 | 26.32% | 5 | 14 | 40.04 | 1.78% | 3.87 | 26.35 |
| 4 | EVGO | 11 | -4.18 | 40 | 27.50% | 11 | 26 | 38.52 | 1.46% | 4.00 | 2.09 |
| 5 | BKKT | 4 | -8.63 | 47 | 31.91% | 15 | 31 | 50.46 | -0.42% | 7.69 | 9.44 |
| 6 | AFRM | 18 | -9.17 | 25 | 40.00% | 10 | 15 | 32.91 | 0.22% | 3.87 | 46.89 |
| 7 | SNAP | 20 | -10.98 | 31 | 22.58% | 7 | 20 | 32.66 | 0.58% | 2.86 | 5.17 |
| 8 | CLSK | 3 | -14.55 | 71 | 32.39% | 23 | 47 | 52.55 | 0.62% | 5.31 | 9.77 |
| 9 | DKNG | 2 | -15.76 | 29 | 31.03% | 9 | 19 | 54.26 | 0.43% | 5.94 | 25.88 |
| 10 | CHPT | 5 | -16.89 | 19 | 36.84% | 7 | 11 | 50.18 | 1.33% | 6.41 | 5.33 |
| 11 | CAST | 13 | -19.99 | 37 | 29.73% | 11 | 26 | 37.22 | 0.91% | 0.00 | 2.80 |
| 12 | HIMS | 7 | -27.49 | 75 | 40.00% | 30 | 45 | 46.41 | 1.93% | 3.42 | 24.80 |
| 13 | CIFR | 16 | -48.81 | 62 | 30.65% | 19 | 43 | 34.63 | 0.75% | 3.25 | 14.08 |
| 14 | EONR | 1 | -52.69 | 94 | 29.79% | 28 | 60 | 90.03 | 4.79% | 2.05 | 1.53 |
| 15 | CLOV | 19 | -64.56 | 59 | 20.34% | 12 | 40 | 32.75 | 0.51% | 3.10 | 1.99 |
| 16 | MVST | 6 | -96.43 | 88 | 20.45% | 18 | 60 | 49.36 | 2.99% | 3.43 | 2.24 |
| 17 | ELPW | 17 | -110.40 | 56 | 23.21% | 13 | 43 | 33.12 | -2.43% | 0.00 | 4.02 |
| 18 | PLTK | 9 | -121.92 | 54 | 9.26% | 5 | 40 | 40.86 | 0.00% | 3.86 | 2.85 |
| 19 | SNDL | 14 | -125.57 | 56 | 10.71% | 6 | 44 | 36.99 | 0.00% | 4.19 | 1.51 |
| 20 | BTBT | 12 | -191.15 | 64 | 14.06% | 9 | 49 | 37.52 | 0.00% | 4.10 | 1.62 |

## Scanner order before backtest

| Scanner rank | Symbol | Score | Momentum | Vol ratio | Close | Reason |
|---:|---|---:|---:|---:|---:|---|
| 1 | EONR | 90.03 | 4.79% | 2.05 | 1.53 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=2.05 |
| 2 | DKNG | 54.26 | 0.43% | 5.94 | 25.88 | EMA9>SMA20, Close>VWAP, Close<=SMA200, VolRatio=5.94 |
| 3 | CLSK | 52.55 | 0.62% | 5.31 | 9.77 | EMA9>SMA20, Close>VWAP, Close<=SMA200, VolRatio=5.31 |
| 4 | BKKT | 50.46 | -0.42% | 7.69 | 9.44 | EMA9>SMA20, Close>VWAP, Close<=SMA200, VolRatio=7.69 |
| 5 | CHPT | 50.18 | 1.33% | 6.41 | 5.33 | EMA9>SMA20, Close<=VWAP, Close>SMA200, VolRatio=6.41 |
| 6 | MVST | 49.36 | 2.99% | 3.43 | 2.24 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.43 |
| 7 | HIMS | 46.41 | 1.93% | 3.42 | 24.80 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.42 |
| 8 | IONQ | 43.31 | 0.05% | 5.04 | 32.97 | EMA9>SMA20, Close<=VWAP, Close<=SMA200, VolRatio=5.04 |
| 9 | PLTK | 40.86 | 0.00% | 3.86 | 2.85 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.86 |
| 10 | UPST | 40.04 | 1.78% | 3.87 | 26.35 | EMA9>SMA20, Close<=VWAP, Close>SMA200, VolRatio=3.87 |
| 11 | EVGO | 38.52 | 1.46% | 4.00 | 2.09 | EMA9>SMA20, Close<=VWAP, Close>SMA200, VolRatio=4.00 |
| 12 | BTBT | 37.52 | 0.00% | 4.10 | 1.62 | EMA9<=SMA20, Close<=VWAP, Close<=SMA200, VolRatio=4.10 |
| 13 | CAST | 37.22 | 0.91% | 0.00 | 2.80 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=0.00 |
| 14 | SNDL | 36.99 | 0.00% | 4.19 | 1.51 | EMA9>SMA20, Close<=VWAP, Close<=SMA200, VolRatio=4.19 |
| 15 | LCID | 35.78 | 0.61% | 3.76 | 9.91 | EMA9>SMA20, Close<=VWAP, Close>SMA200, VolRatio=3.76 |
| 16 | CIFR | 34.63 | 0.75% | 3.25 | 14.08 | EMA9>SMA20, Close>VWAP, Close<=SMA200, VolRatio=3.25 |
| 17 | ELPW | 33.12 | -2.43% | 0.00 | 4.02 | EMA9<=SMA20, Close>VWAP, Close<=SMA200, VolRatio=0.00 |
| 18 | AFRM | 32.91 | 0.22% | 3.87 | 46.89 | EMA9<=SMA20, Close<=VWAP, Close<=SMA200, VolRatio=3.87 |
| 19 | CLOV | 32.75 | 0.51% | 3.10 | 1.99 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=3.10 |
| 20 | SNAP | 32.66 | 0.58% | 2.86 | 5.17 | EMA9>SMA20, Close>VWAP, Close>SMA200, VolRatio=2.86 |

## Backtest log files

- IONQ: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_IONQ_1m_simple-momentum_20260626_092737.log`
- LCID: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_LCID_1m_simple-momentum_20260626_092737.log`
- UPST: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_UPST_1m_simple-momentum_20260626_092737.log`
- EVGO: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_EVGO_1m_simple-momentum_20260626_092737.log`
- BKKT: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_BKKT_1m_simple-momentum_20260626_092737.log`
- AFRM: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_AFRM_1m_simple-momentum_20260626_092737.log`
- SNAP: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_SNAP_1m_simple-momentum_20260626_092737.log`
- CLSK: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CLSK_1m_simple-momentum_20260626_092737.log`
- DKNG: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_DKNG_1m_simple-momentum_20260626_092737.log`
- CHPT: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CHPT_1m_simple-momentum_20260626_092737.log`
- CAST: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CAST_1m_simple-momentum_20260626_092737.log`
- HIMS: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_HIMS_1m_simple-momentum_20260626_092737.log`
- CIFR: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CIFR_1m_simple-momentum_20260626_092737.log`
- EONR: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_EONR_1m_simple-momentum_20260626_092737.log`
- CLOV: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_CLOV_1m_simple-momentum_20260626_092737.log`
- MVST: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_MVST_1m_simple-momentum_20260626_092737.log`
- ELPW: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_ELPW_1m_simple-momentum_20260626_092737.log`
- PLTK: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_PLTK_1m_simple-momentum_20260626_092737.log`
- SNDL: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_SNDL_1m_simple-momentum_20260626_092737.log`
- BTBT: `D:\Site\sailor\src\Sailor.App\Logs\Backtest\backtest_BTBT_1m_simple-momentum_20260626_092737.log`

