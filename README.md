SAILOR-018 minimal V21 angle conduct update

Apply over D:\Site\sailor.

Changes:
- V21 direct initial entry from completed 15m EMA9 angle rule.
- Re-entry after the first completed trade still uses the existing confirmation rules.
- Uses existing completed 15m signal candle logic.
- Keeps existing profile market window: last entry 15:45 ET and force-flat 15:55 ET.

Commands:
cd D:\Site\sailor
dotnet clean
dotnet build
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v21-15minutes
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps 0 v21-15minutes

git status
git add .
git commit -m "SAILOR-018 Apply minimal V21 completed candle angle entry"
git push origin main
