SAILOR-020 L1/L2 snapshot layer

Apply over D:\Site\sailor.

Commands:
cd D:\Site\sailor
dotnet clean
dotnet build
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v16-sqzbreakout
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m sailor-trend-volume
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps

Commit:
git status
git add .
git commit -m "SAILOR-020 Add L1 L2 snapshot layer"
git push origin main

Notes:
- No Harvester live runtime classes are imported.
- Backtest uses synthetic L1/L2 snapshots derived from 1m bars.
- Synthetic snapshots are advisory by default, so they enrich logs/reasons without rejecting trades.
- Later paper/live can feed real IBKR L1/L2 into the same snapshot model.
