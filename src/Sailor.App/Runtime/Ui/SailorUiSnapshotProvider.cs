using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;

namespace Sailor.App.Runtime.Ui;

public sealed class SailorUiSnapshotProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SailorRuntimeMode _mode;
    private readonly int _maxScannerRows;
    private readonly int _maxActiveStrategies;
    private readonly bool _controlsEnabled;
    private readonly string? _account;
    private readonly string _repositoryRoot;
    private readonly SailorUiDesiredStateStore _desiredStateStore;

    public SailorUiSnapshotProvider(
        SailorRuntimeMode mode,
        int maxScannerRows = SailorUiContract.DefaultScannerRows,
        int maxActiveStrategies = SailorUiContract.DefaultMaxActiveStrategies,
        bool controlsEnabled = false,
        string? account = null)
    {
        _mode = mode;
        _maxScannerRows = Math.Max(1, maxScannerRows);
        _maxActiveStrategies = Math.Max(1, maxActiveStrategies);
        _controlsEnabled = controlsEnabled && mode == SailorRuntimeMode.Paper;
        _account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        _repositoryRoot = FindRepositoryRoot();
        _desiredStateStore = new SailorUiDesiredStateStore(mode, _account, _maxActiveStrategies, _repositoryRoot);
    }

    public SailorUiSnapshot ReadSnapshot()
    {
        var warnings = new List<string>();
        IReadOnlyList<SailorUiStrategyOption> strategies = LoadStrategyOptions(warnings);
        SailorUiDesiredStateSnapshot desiredState = _mode == SailorRuntimeMode.Live
            ? new SailorUiDesiredStateSnapshot(_mode.ToDisplayName(), _account, DateTimeOffset.MinValue, _maxActiveStrategies, Array.Empty<SailorUiDesiredStateRow>())
            : _desiredStateStore.LoadSnapshot();
        IReadOnlyList<SailorUiScannerCandidate> scannerCandidates = LoadScannerCandidates(warnings);
        IReadOnlyDictionary<string, SailorUiScannerCandidate> scannerBySymbol = scannerCandidates
            .GroupBy(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(candidate => candidate.Rank).First(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, SailorUiMarketPricePoint> marketPriceBySymbol = SailorUiMarketPriceStore.LoadLatest(ModeLogRoot(), DateTimeOffset.UtcNow, warnings);

        TradeLifecycleRegistrySnapshot registry = new TradeLifecycleRegistryStore(_mode).LoadSnapshot();
        IReadOnlyList<BrokerMirrorPositionRow> brokerPositions = LoadBrokerPositions(warnings);
        IReadOnlyDictionary<string, BrokerMirrorPositionRow> positionBySymbol = brokerPositions
            .Where(position => position.Quantity != 0)
            .GroupBy(position => position.NormalizedSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        IReadOnlyList<TradeLifecycle> todayOrActiveTrades = registry.Trades
            .Where(trade => trade.IsActive || trade.TradeDate == today || trade.UpdatedUtc.UtcDateTime.Date == DateTime.UtcNow.Date)
            .OrderByDescending(trade => trade.UpdatedUtc)
            .ToArray();

        var activeSymbols = positionBySymbol.Keys
            .Concat(todayOrActiveTrades.Select(trade => trade.NormalizedSymbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activeRows = new List<SailorUiTradeRow>();
        foreach (string symbol in activeSymbols)
        {
            positionBySymbol.TryGetValue(symbol, out BrokerMirrorPositionRow? brokerPosition);
            TradeLifecycle? lifecycle = todayOrActiveTrades.FirstOrDefault(trade => trade.NormalizedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            scannerBySymbol.TryGetValue(symbol, out SailorUiScannerCandidate? candidate);

            int quantity = brokerPosition?.Quantity ?? lifecycle?.BrokerQuantity ?? 0;
            decimal open = brokerPosition?.AverageCost > 0m
                ? brokerPosition.AverageCost
                : lifecycle?.BrokerAveragePrice > 0m
                    ? lifecycle.BrokerAveragePrice
                    : 0m;
            marketPriceBySymbol.TryGetValue(symbol, out SailorUiMarketPricePoint? marketPrice);
            decimal price = marketPrice?.Price > 0m
                ? marketPrice.Price
                : candidate?.Price > 0m
                    ? candidate.Price
                    : open;
            decimal marketValue = quantity * price;
            decimal buyValue = Math.Abs(quantity) * open;
            decimal dailyPnl = open > 0m && price > 0m && quantity != 0
                ? (price - open) * quantity
                : 0m;
            SailorUiDesiredStateRow? desiredRow = desiredState.FindRow(symbol);
            string strategy = !string.IsNullOrWhiteSpace(desiredRow?.SelectedStrategy)
                ? desiredRow!.SelectedStrategy
                : lifecycle?.ProfileName ?? ResolveDefaultStrategy(strategies);
            bool tradeEnabled = desiredRow?.DesiredTradeEnabled ?? (lifecycle?.IsActive == true || quantity != 0);
            bool stale = marketPrice is not null ? marketPrice.IsStale : candidate?.PriceStale ?? true;
            string priceSource = marketPrice is not null
                ? marketPrice.Source
                : candidate is null
                    ? "broker-open-price"
                    : "scanner-current-1m-decision-price";
            string status = lifecycle?.Status.ToDisplayName() ?? (quantity == 0 ? "flat" : "open");
            string reason = lifecycle?.LastReason ?? candidate?.Reason ?? "broker/scanner state loaded for read-only UI";

            activeRows.Add(new SailorUiTradeRow(
                dailyPnl,
                candidate?.Rank,
                symbol,
                quantity,
                marketValue,
                buyValue,
                open,
                price,
                stale,
                priceSource,
                tradeEnabled,
                strategy,
                strategies,
                candidate?.Volume ?? 0,
                status,
                reason));
        }

        activeRows = activeRows
            .OrderByDescending(row => row.DailyPnl)
            .ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeSymbolSet = new HashSet<string>(activeRows.Select(row => row.Symbol), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<SailorUiScannerRow> scannerRows = scannerCandidates
            .Where(candidate => !activeSymbolSet.Contains(candidate.Symbol))
            .Take(_maxScannerRows)
            .Select(candidate =>
            {
                SailorUiDesiredStateRow? desiredRow = desiredState.FindRow(candidate.Symbol);
                marketPriceBySymbol.TryGetValue(candidate.Symbol, out SailorUiMarketPricePoint? marketPrice);
                decimal displayPrice = marketPrice?.Price > 0m ? marketPrice.Price : candidate.Price;
                bool priceStale = marketPrice is not null ? marketPrice.IsStale : candidate.PriceStale;
                return new SailorUiScannerRow(
                    candidate.Rank,
                    candidate.Symbol,
                    desiredRow?.DesiredTradeEnabled ?? false,
                    !string.IsNullOrWhiteSpace(desiredRow?.SelectedStrategy) ? desiredRow!.SelectedStrategy : ResolveDefaultStrategy(strategies),
                    strategies,
                    candidate.Volume,
                    displayPrice,
                    priceStale,
                    candidate.SelectedSide,
                    candidate.FinalScore,
                    candidate.Status,
                    candidate.Reason);
            })
            .ToArray();

        decimal realized = LoadRealizedPnlToday(warnings);
        decimal unrealized = activeRows.Sum(row => row.DailyPnl);
        bool pnlStale = activeRows.Any(row => row.PriceStale) || scannerCandidates.Any(candidate => candidate.PriceStale);
        string staleReason = pnlStale
            ? "one or more prices are stale, loaded from historical scanner files, or missing fresh market snapshots"
            : "current market snapshot/scanner decision prices available";

        string statusText = warnings.Count == 0
            ? "OK"
            : "WARN";
        string sourceSummary = $"scannerRows={scannerCandidates.Count} activeRows={activeRows.Count} strategies={strategies.Count} state={_mode.ToDisplayName()}";
        IReadOnlyList<string> activeDesiredStrategies = _mode == SailorRuntimeMode.Live
            ? Array.Empty<string>()
            : desiredState.ActiveStrategies;

        return new SailorUiSnapshot(
            _mode.ToDisplayName(),
            DateTimeOffset.UtcNow,
            statusText,
            new SailorUiPnlSection(realized + unrealized, unrealized, realized, "USD", pnlStale, staleReason),
            activeRows,
            scannerRows,
            strategies,
            _maxActiveStrategies,
            SailorUiContract.DefaultRefreshMilliseconds,
            _controlsEnabled,
            SailorUiLiveHardening.ResolveControlMode(_mode, _controlsEnabled),
            activeDesiredStrategies,
            desiredState.UpdatedUtc == DateTimeOffset.MinValue ? "n/a" : desiredState.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture),
            sourceSummary,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray());
    }

    private IReadOnlyList<BrokerMirrorPositionRow> LoadBrokerPositions(List<string> warnings)
    {
        string path = Path.Combine(_repositoryRoot, "state", _mode.ToDisplayName(), "broker-mirror", "broker_state_mirror_latest.json");
        if (!File.Exists(path))
        {
            warnings.Add($"Broker mirror not found: {Relative(path)}");
            return Array.Empty<BrokerMirrorPositionRow>();
        }

        try
        {
            string json = File.ReadAllText(path);
            BrokerStateMirrorSnapshot? snapshot = JsonSerializer.Deserialize<BrokerStateMirrorSnapshot>(json, JsonOptions);
            return snapshot?.Positions ?? Array.Empty<BrokerMirrorPositionRow>();
        }
        catch (Exception ex)
        {
            warnings.Add($"Broker mirror could not be read: {ex.Message}");
            return Array.Empty<BrokerMirrorPositionRow>();
        }
    }

    private IReadOnlyList<SailorUiScannerCandidate> LoadScannerCandidates(List<string> warnings)
    {
        string scannerDirectory = Path.Combine(ModeLogRoot(), "Scanner");
        string? path = Directory.Exists(scannerDirectory)
            ? Directory.GetFiles(scannerDirectory, "scanner_*.csv")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            warnings.Add($"Scanner CSV not found: {Relative(scannerDirectory)}");
            return Array.Empty<SailorUiScannerCandidate>();
        }

        Dictionary<string, object?> scanListEvidence = LoadLatestJsonObject(Path.Combine(ModeLogRoot(), "ScanList", "scanlist_latest.json"));
        HashSet<string> staleSelectedSymbols = ReadStringArray(scanListEvidence, "notReadySelectedSymbols");
        string dataQuality = ReadString(scanListEvidence, "dataQualityStatus", string.Empty);
        bool scanListBlocked = dataQuality.Equals("Blocked", StringComparison.OrdinalIgnoreCase);
        bool csvFileStale = DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) > TimeSpan.FromMinutes(5);

        try
        {
            IReadOnlyList<Dictionary<string, string>> rows = SailorUiCsv.Read(path);
            var candidates = new List<SailorUiScannerCandidate>();
            foreach (Dictionary<string, string> row in rows)
            {
                string symbol = ReadCsv(row, "Symbol").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                int rank = ReadCsvInt(row, "Rank", candidates.Count + 1);
                string status = ReadCsv(row, "Status", "Unknown");
                string selectedSide = ReadCsv(row, "SelectedSide", "n/a");
                decimal finalScore = ReadCsvDecimal(row, "FinalScore", 0m);
                decimal price = ReadCsvDecimal(row, "Close", 0m);
                long volume = ReadCsvLong(row, "Volume", 0);
                string reason = ReadCsv(row, "Reason", ReadCsv(row, "LegacyBlockReasons", string.Empty));
                bool priceStale = csvFileStale || (scanListBlocked && staleSelectedSymbols.Contains(symbol));

                candidates.Add(new SailorUiScannerCandidate(
                    rank,
                    symbol,
                    status,
                    selectedSide,
                    finalScore,
                    price,
                    volume,
                    priceStale,
                    reason));
            }

            return candidates
                .OrderBy(candidate => candidate.Rank)
                .ThenByDescending(candidate => candidate.FinalScore)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Scanner CSV could not be read: {ex.Message}");
            return Array.Empty<SailorUiScannerCandidate>();
        }
    }

    private IReadOnlyList<SailorUiStrategyOption> LoadStrategyOptions(List<string> warnings)
    {
        IReadOnlyList<SailorUiStrategyOption> fromHtml = LoadStrategyOptionsFromHtmlReport(warnings);
        if (fromHtml.Count > 0)
        {
            return fromHtml;
        }

        IReadOnlyList<SailorUiStrategyOption> fromHarshSummary = LoadStrategyOptionsFromHarshSummary(warnings);
        if (fromHarshSummary.Count > 0)
        {
            return fromHarshSummary;
        }

        warnings.Add("Strategy report not found; using built-in strategy fallback order.");
        return BuiltInStrategyFallback();
    }

    private IReadOnlyList<SailorUiStrategyOption> LoadStrategyOptionsFromHtmlReport(List<string> warnings)
    {
        string htmlDirectory = SailorLogPaths.BacktestHtml;
        string? path = Directory.Exists(htmlDirectory)
            ? Directory.GetFiles(htmlDirectory, "strategy_trades_report_*.html")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<SailorUiStrategyOption>();
        }

        try
        {
            string html = File.ReadAllText(path);
            var rows = new List<SailorUiStrategyOption>();
            foreach (Match tr in Regex.Matches(html, "<tr[^>]*data-ge50=\\\"[^\\\"]*\\\"[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                List<string> cells = Regex.Matches(tr.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                    .Select(match => CleanHtml(match.Groups[1].Value))
                    .ToList();
                if (cells.Count < 19)
                {
                    continue;
                }

                string strategy = cells[0];
                string profileName = StrategyNameToProfileName(strategy);
                rows.Add(new SailorUiStrategyOption(
                    strategy,
                    profileName,
                    cells[1],
                    cells[2],
                    ParseDecimal(cells[12]),
                    ParseInt(cells[4]),
                    ParseDecimal(cells[6].Replace("%", string.Empty, StringComparison.Ordinal)),
                    ParseDecimal(cells[7])));
            }

            return rows
                .GroupBy(row => row.ProfileName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(row => row.TotalPnl).First())
                .OrderByDescending(row => row.TotalPnl)
                .ThenByDescending(row => row.Trades)
                .ThenBy(row => row.Strategy, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Strategy HTML report could not be read: {ex.Message}");
            return Array.Empty<SailorUiStrategyOption>();
        }
    }

    private IReadOnlyList<SailorUiStrategyOption> LoadStrategyOptionsFromHarshSummary(List<string> warnings)
    {
        string path = Path.Combine(ModeLogRoot(), "HarshConduct", "harsh_conduct_summary_latest.csv");
        if (!File.Exists(path))
        {
            return Array.Empty<SailorUiStrategyOption>();
        }

        try
        {
            return SailorUiCsv.Read(path)
                .Select(row => new SailorUiStrategyOption(
                    ReadCsv(row, "Strategy", "unknown"),
                    StrategyNameToProfileName(ReadCsv(row, "Strategy", "unknown")),
                    ReadCsv(row, "Variant", "unknown"),
                    ReadCsv(row, "Style", "unknown"),
                    ReadCsvDecimal(row, "TotalPnL$", 0m),
                    ReadCsvInt(row, "Trades", 0),
                    ReadCsvDecimal(row, "WinRate", 0m),
                    ReadCsvDecimal(row, "PF", 0m)))
                .OrderByDescending(row => row.TotalPnl)
                .ThenByDescending(row => row.Trades)
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Harsh conduct summary could not be read: {ex.Message}");
            return Array.Empty<SailorUiStrategyOption>();
        }
    }

    private decimal LoadRealizedPnlToday(List<string> warnings)
    {
        string path = Path.Combine(ModeLogRoot(), "HarshConduct", $"harsh_conduct_trades_{DateTime.UtcNow:yyyyMMdd}.csv");
        if (!File.Exists(path))
        {
            return 0m;
        }

        try
        {
            return SailorUiCsv.Read(path).Sum(row => ReadCsvDecimal(row, "RealizedPnL", 0m));
        }
        catch (Exception ex)
        {
            warnings.Add($"Realized P&L CSV could not be read: {ex.Message}");
            return 0m;
        }
    }

    private string ModeLogRoot()
        => _mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper;

    private string Relative(string path)
    {
        try
        {
            return Path.GetRelativePath(_repositoryRoot, path);
        }
        catch
        {
            return path;
        }
    }

    private static string ResolveDefaultStrategy(IReadOnlyList<SailorUiStrategyOption> strategies)
        => strategies.Count > 0 ? strategies[0].ProfileName : "v21-15minutes";

    private static Dictionary<string, object?> LoadLatestJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return JsonElementToDictionary(document.RootElement);
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out long number) ? number : property.Value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => property.Value.EnumerateArray().Select(item => item.ToString()).ToArray(),
                _ => property.Value.ToString()
            };
        }

        return result;
    }

    private static HashSet<string> ReadStringArray(Dictionary<string, object?> values, string key)
    {
        if (values.TryGetValue(key, out object? value) && value is string[] array)
        {
            return array.Select(item => item.Trim().ToUpperInvariant()).Where(item => item.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadString(Dictionary<string, object?> values, string key, string fallback)
        => values.TryGetValue(key, out object? value) ? value?.ToString() ?? fallback : fallback;

    private static string ReadCsv(Dictionary<string, string> row, string column, string fallback = "")
        => row.TryGetValue(column, out string? value) ? value : fallback;

    private static int ReadCsvInt(Dictionary<string, string> row, string column, int fallback)
        => int.TryParse(ReadCsv(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static long ReadCsvLong(Dictionary<string, string> row, string column, long fallback)
        => long.TryParse(ReadCsv(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : fallback;

    private static decimal ReadCsvDecimal(Dictionary<string, string> row, string column, decimal fallback)
        => decimal.TryParse(ReadCsv(row, column), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) ? value : fallback;

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value.Replace("%", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : 0m;

    private static int ParseInt(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static string CleanHtml(string html)
    {
        string withoutTags = Regex.Replace(html, "<.*?>", string.Empty, RegexOptions.Singleline);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string StrategyNameToProfileName(string strategy)
        => SailorUiStrategyProfileNames.Normalize(strategy);

    private static IReadOnlyList<SailorUiStrategyOption> BuiltInStrategyFallback()
    {
        string[] strategyNames =
        [
            "V18-Silver",
            "V20-GEN001-ChoppyShield",
            "Harvester-ConductV9",
            "V15-ShortCap",
            "V19-PurpleCloud",
            "V21-15Minutes",
            "V16-SqzBreakout",
            "V22-15Minutes",
            "V23-5Minutes",
            "V24-5Minutes",
            "V13",
            "Sailor-ConductV3",
            "Conduct-V3",
            "Harvester-ConductV3",
            "V2-Conduct",
            "Sailor-TrendVolume",
            "V12",
            "V10-Hybrid",
            "V17-HybridFlow",
            "V1-First",
            "V14-SmallCap",
            "SimpleMomentum"
        ];

        return strategyNames
            .Select((strategy, index) => new SailorUiStrategyOption(
                strategy,
                StrategyNameToProfileName(strategy),
                "fallback",
                "fallback",
                -index,
                0,
                0m,
                0m))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}

internal static class SailorUiCsv
{
    public static IReadOnlyList<Dictionary<string, string>> Read(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        string[] header = Split(lines[0]).Select(item => item.Trim()).ToArray();
        var rows = new List<Dictionary<string, string>>();
        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] cells = Split(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                row[header[i]] = i < cells.Length ? cells[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string[] Split(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }
}
