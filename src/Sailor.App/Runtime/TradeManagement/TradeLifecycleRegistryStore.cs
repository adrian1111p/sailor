using System.Text.Json;
using Sailor.App.Broker.Orders;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.TradeManagement;

public sealed class TradeLifecycleRegistryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;

    public TradeLifecycleRegistryStore(SailorRuntimeMode mode)
    {
        _mode = mode;
        RegistryDirectory = EnsureDirectory(Path.Combine(RepositoryRoot, "state", mode.ToDisplayName(), "trades"));
        LatestJsonPath = Path.Combine(RegistryDirectory, "trade_registry_latest.json");
        DailyJsonlPath = Path.Combine(RegistryDirectory, $"trade_registry_{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public string RegistryDirectory { get; }

    public string LatestJsonPath { get; }

    public string DailyJsonlPath { get; }

    public TradeLifecycleRegistrySnapshot LoadSnapshot()
    {
        if (!File.Exists(LatestJsonPath))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new TradeLifecycleRegistrySnapshot(_mode.ToDisplayName(), now, now, Array.Empty<TradeLifecycle>());
        }

        try
        {
            string json = File.ReadAllText(LatestJsonPath);
            TradeLifecycleRegistrySnapshot? snapshot = JsonSerializer.Deserialize<TradeLifecycleRegistrySnapshot>(json, JsonOptions);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }
        catch
        {
            // Keep the runtime safe and non-blocking. A corrupt registry must not stop close-only exits.
        }

        DateTimeOffset fallbackNow = DateTimeOffset.UtcNow;
        return new TradeLifecycleRegistrySnapshot(_mode.ToDisplayName(), fallbackNow, fallbackNow, Array.Empty<TradeLifecycle>());
    }

    public TradeLifecycle RegisterRuntimeSession(
        string symbol,
        string profileName,
        SailorTradeOrigin origin,
        string? scannerSlotId,
        int brokerQuantity,
        decimal brokerAveragePrice,
        string? timeframe,
        string? account,
        string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedSymbol = NormalizeSymbol(symbol);
        var snapshot = LoadSnapshot();
        var trades = snapshot.Trades.ToList();
        TradeLifecycle? existing = FindActiveTrade(trades, normalizedSymbol, scannerSlotId);
        TradeLifecycleStatus status = brokerQuantity == 0
            ? TradeLifecycleStatus.PendingEntry
            : TradeLifecycleStatus.Open;

        TradeLifecycle updated = existing is null
            ? new TradeLifecycle(
                CreateTradeId(normalizedSymbol, now),
                normalizedSymbol,
                NormalizeProfile(profileName),
                origin,
                NormalizeOptional(scannerSlotId),
                status,
                brokerQuantity,
                brokerAveragePrice,
                ManualStoppedForDay: false,
                DateOnly.FromDateTime(now.UtcDateTime),
                now,
                now,
                NormalizeOptional(timeframe),
                NormalizeOptional(account),
                LastOrderIntentId: null,
                LastBrokerOrderId: null,
                LastReason: reason,
                CompletedUtc: null)
            : existing with
            {
                ProfileName = NormalizeProfile(profileName),
                Origin = existing.Origin == SailorTradeOrigin.ScannerOwned ? existing.Origin : origin,
                ScannerSlotId = existing.ScannerSlotId ?? NormalizeOptional(scannerSlotId),
                Status = existing.Status.IsActive() ? (existing.BrokerQuantity == 0 && brokerQuantity != 0 ? TradeLifecycleStatus.Open : existing.Status) : status,
                BrokerQuantity = brokerQuantity != 0 ? brokerQuantity : existing.BrokerQuantity,
                BrokerAveragePrice = brokerAveragePrice > 0m ? brokerAveragePrice : existing.BrokerAveragePrice,
                Timeframe = existing.Timeframe ?? NormalizeOptional(timeframe),
                Account = existing.Account ?? NormalizeOptional(account),
                LastReason = reason,
                UpdatedUtc = now
            };

        Upsert(trades, updated);
        Save(trades, updated, "runtime-session", reason, null, null, now);
        return updated;
    }

    public TradeLifecycle ApplyOrderReceipt(
        SailorOrderIntent intent,
        SailorOrderReceipt receipt,
        SailorTradeOrigin defaultOrigin,
        int positionQuantityAfter,
        decimal averagePriceAfter,
        string? scannerSlotId,
        string? sourceMessage)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedSymbol = NormalizeSymbol(intent.Symbol);
        var snapshot = LoadSnapshot();
        var trades = snapshot.Trades.ToList();
        TradeLifecycle? existing = FindActiveTrade(trades, normalizedSymbol, scannerSlotId)
            ?? (intent.Side.IsExit() ? FindMostRecentTrade(trades, normalizedSymbol) : null);

        TradeLifecycleStatus nextStatus = DetermineStatus(intent, receipt, positionQuantityAfter);
        if (existing is not null && existing.ManualStoppedForDay && nextStatus.IsActive())
        {
            nextStatus = TradeLifecycleStatus.StoppedForDay;
        }

        int brokerQuantity = positionQuantityAfter;
        decimal brokerAveragePrice = averagePriceAfter > 0m ? averagePriceAfter : existing?.BrokerAveragePrice ?? 0m;
        if (nextStatus is TradeLifecycleStatus.ClosedByStrategy or TradeLifecycleStatus.ClosedManually or TradeLifecycleStatus.StoppedForDay)
        {
            brokerQuantity = 0;
            brokerAveragePrice = 0m;
        }

        TradeLifecycle updated = existing is null
            ? new TradeLifecycle(
                CreateTradeId(normalizedSymbol, now),
                normalizedSymbol,
                NormalizeProfile(intent.StrategyName),
                defaultOrigin,
                NormalizeOptional(scannerSlotId),
                nextStatus,
                brokerQuantity,
                brokerAveragePrice,
                ManualStoppedForDay: false,
                DateOnly.FromDateTime(now.UtcDateTime),
                now,
                now,
                Timeframe: null,
                Account: NormalizeOptional(intent.Account),
                LastOrderIntentId: intent.NormalizedIntentId,
                LastBrokerOrderId: NormalizeOptional(receipt.BrokerOrderId),
                LastReason: sourceMessage ?? receipt.Message,
                CompletedUtc: nextStatus.IsClosed() ? now : null)
            : existing with
            {
                ProfileName = NormalizeProfile(intent.StrategyName),
                ScannerSlotId = existing.ScannerSlotId ?? NormalizeOptional(scannerSlotId),
                Status = nextStatus,
                BrokerQuantity = brokerQuantity,
                BrokerAveragePrice = brokerAveragePrice,
                Account = existing.Account ?? NormalizeOptional(intent.Account),
                LastOrderIntentId = intent.NormalizedIntentId,
                LastBrokerOrderId = NormalizeOptional(receipt.BrokerOrderId),
                LastReason = sourceMessage ?? receipt.Message,
                UpdatedUtc = now,
                CompletedUtc = nextStatus.IsClosed() ? now : existing.CompletedUtc
            };

        Upsert(trades, updated);
        Save(trades, updated, "order-receipt", sourceMessage ?? receipt.Message, intent.NormalizedIntentId, receipt.BrokerOrderId, now);
        return updated;
    }

    public TradeLifecycle MarkManualStoppedForDay(string symbol, string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedSymbol = NormalizeSymbol(symbol);
        var snapshot = LoadSnapshot();
        var trades = snapshot.Trades.ToList();
        TradeLifecycle? existing = FindActiveTrade(trades, normalizedSymbol, scannerSlotId: null)
            ?? FindMostRecentTrade(trades, normalizedSymbol);

        TradeLifecycle updated = existing is null
            ? new TradeLifecycle(
                CreateTradeId(normalizedSymbol, now),
                normalizedSymbol,
                "unknown",
                SailorTradeOrigin.UnknownBroker,
                ScannerSlotId: null,
                TradeLifecycleStatus.StoppedForDay,
                BrokerQuantity: 0,
                BrokerAveragePrice: 0m,
                ManualStoppedForDay: true,
                DateOnly.FromDateTime(now.UtcDateTime),
                now,
                now,
                LastReason: reason,
                CompletedUtc: now)
            : existing with
            {
                Status = TradeLifecycleStatus.StoppedForDay,
                BrokerQuantity = 0,
                BrokerAveragePrice = 0m,
                ManualStoppedForDay = true,
                LastReason = reason,
                UpdatedUtc = now,
                CompletedUtc = now
            };

        Upsert(trades, updated);
        Save(trades, updated, "manual-stop-for-day", reason, null, null, now);
        return updated;
    }


    public TradeLifecycle ApplyBrokerMirrorPosition(
        string symbol,
        string profileName,
        SailorTradeOrigin origin,
        int brokerQuantity,
        decimal brokerAveragePrice,
        string? account,
        string source,
        string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedSymbol = NormalizeSymbol(symbol);
        var snapshot = LoadSnapshot();
        var trades = snapshot.Trades.ToList();
        TradeLifecycle? existing = FindActiveTrade(trades, normalizedSymbol, scannerSlotId: null)
            ?? FindMostRecentTrade(trades, normalizedSymbol);

        TradeLifecycleStatus status = brokerQuantity == 0
            ? TradeLifecycleStatus.PendingEntry
            : TradeLifecycleStatus.Open;

        SailorTradeOrigin resolvedOrigin = existing is null
            ? origin
            : existing.Origin is SailorTradeOrigin.UnknownBroker
                ? origin
                : existing.Origin;

        TradeLifecycle updated = existing is null || existing.Status.IsClosed()
            ? new TradeLifecycle(
                CreateTradeId(normalizedSymbol, now),
                normalizedSymbol,
                NormalizeProfile(profileName),
                resolvedOrigin,
                ScannerSlotId: null,
                status,
                brokerQuantity,
                brokerAveragePrice,
                ManualStoppedForDay: false,
                DateOnly.FromDateTime(now.UtcDateTime),
                now,
                now,
                Timeframe: null,
                Account: NormalizeOptional(account),
                LastOrderIntentId: null,
                LastBrokerOrderId: null,
                LastReason: reason,
                CompletedUtc: null)
            : existing with
            {
                ProfileName = string.Equals(existing.ProfileName, "unknown", StringComparison.OrdinalIgnoreCase) || string.Equals(existing.ProfileName, "broker-mirror", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeProfile(profileName)
                    : existing.ProfileName,
                Origin = resolvedOrigin,
                Status = status,
                BrokerQuantity = brokerQuantity,
                BrokerAveragePrice = brokerAveragePrice,
                ManualStoppedForDay = false,
                Account = existing.Account ?? NormalizeOptional(account),
                LastReason = reason,
                UpdatedUtc = now,
                CompletedUtc = null
            };

        Upsert(trades, updated);
        Save(trades, updated, $"broker-mirror-position:{source}", reason, null, null, now);
        return updated;
    }

    public TradeLifecycle MarkBrokerMirrorClosedManually(string symbol, string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedSymbol = NormalizeSymbol(symbol);
        var snapshot = LoadSnapshot();
        var trades = snapshot.Trades.ToList();
        TradeLifecycle? existing = FindActiveTrade(trades, normalizedSymbol, scannerSlotId: null)
            ?? FindMostRecentTrade(trades, normalizedSymbol);

        TradeLifecycle updated = existing is null
            ? new TradeLifecycle(
                CreateTradeId(normalizedSymbol, now),
                normalizedSymbol,
                "broker-mirror",
                SailorTradeOrigin.UnknownBroker,
                ScannerSlotId: null,
                TradeLifecycleStatus.ClosedManually,
                BrokerQuantity: 0,
                BrokerAveragePrice: 0m,
                ManualStoppedForDay: true,
                DateOnly.FromDateTime(now.UtcDateTime),
                now,
                now,
                LastReason: reason,
                CompletedUtc: now)
            : existing with
            {
                Status = TradeLifecycleStatus.ClosedManually,
                BrokerQuantity = 0,
                BrokerAveragePrice = 0m,
                ManualStoppedForDay = true,
                LastReason = reason,
                UpdatedUtc = now,
                CompletedUtc = now
            };

        Upsert(trades, updated);
        Save(trades, updated, "broker-mirror-manual-close", reason, null, null, now);
        return updated;
    }

    private static TradeLifecycleStatus DetermineStatus(
        SailorOrderIntent intent,
        SailorOrderReceipt receipt,
        int positionQuantityAfter)
    {
        if (receipt.Status is SailorOrderStatus.Failed or SailorOrderStatus.Rejected)
        {
            return TradeLifecycleStatus.Error;
        }

        if (intent.Side.IsExit())
        {
            if (receipt.Status is SailorOrderStatus.Submitted or SailorOrderStatus.PendingSubmit or SailorOrderStatus.PartiallyFilled)
            {
                return TradeLifecycleStatus.ExitSubmitted;
            }

            return positionQuantityAfter == 0
                ? TradeLifecycleStatus.ClosedByStrategy
                : TradeLifecycleStatus.Open;
        }

        if (intent.Side.OpensLong() || intent.Side.OpensShort())
        {
            if (receipt.Status is SailorOrderStatus.Submitted or SailorOrderStatus.PendingSubmit)
            {
                return TradeLifecycleStatus.EntrySubmitted;
            }

            return positionQuantityAfter == 0 && receipt.FilledQuantity <= 0 && receipt.Status != SailorOrderStatus.DryRun
                ? TradeLifecycleStatus.EntrySubmitted
                : TradeLifecycleStatus.Open;
        }

        return positionQuantityAfter == 0 ? TradeLifecycleStatus.PendingEntry : TradeLifecycleStatus.Open;
    }

    private void Save(
        List<TradeLifecycle> trades,
        TradeLifecycle updated,
        string eventType,
        string? message,
        string? intentId,
        string? brokerOrderId,
        DateTimeOffset observedUtc)
    {
        var snapshot = new TradeLifecycleRegistrySnapshot(
            _mode.ToDisplayName(),
            observedUtc,
            observedUtc,
            trades
                .OrderByDescending(trade => trade.IsActive)
                .ThenBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(trade => trade.CreatedUtc)
                .ToArray());

        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(snapshot, JsonOptions));

        string eventIdSource = $"TLE-{observedUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        string eventId = eventIdSource.Length <= 37 ? eventIdSource : eventIdSource[..37];
        var evt = new TradeLifecycleEvent(
            eventId,
            updated.TradeId,
            updated.NormalizedSymbol,
            eventType,
            updated.Status,
            updated.Origin,
            updated.BrokerQuantity,
            updated.BrokerAveragePrice,
            observedUtc,
            updated.ScannerSlotId,
            NormalizeOptional(intentId),
            NormalizeOptional(brokerOrderId),
            NormalizeOptional(message));

        File.AppendAllText(DailyJsonlPath, JsonSerializer.Serialize(evt, JsonOptions).Replace(Environment.NewLine, string.Empty) + Environment.NewLine);
    }

    private static void Upsert(List<TradeLifecycle> trades, TradeLifecycle updated)
    {
        int index = trades.FindIndex(trade => trade.TradeId.Equals(updated.TradeId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            trades[index] = updated;
            return;
        }

        trades.Add(updated);
    }

    private static TradeLifecycle? FindActiveTrade(List<TradeLifecycle> trades, string normalizedSymbol, string? scannerSlotId)
    {
        IEnumerable<TradeLifecycle> query = trades
            .Where(trade => trade.IsActive && trade.NormalizedSymbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase));

        string? normalizedSlot = NormalizeOptional(scannerSlotId);
        if (!string.IsNullOrWhiteSpace(normalizedSlot))
        {
            TradeLifecycle? slotMatch = query.FirstOrDefault(trade => string.Equals(trade.ScannerSlotId, normalizedSlot, StringComparison.OrdinalIgnoreCase));
            if (slotMatch is not null)
            {
                return slotMatch;
            }
        }

        return query.OrderByDescending(trade => trade.UpdatedUtc).FirstOrDefault();
    }

    private static TradeLifecycle? FindMostRecentTrade(List<TradeLifecycle> trades, string normalizedSymbol)
        => trades
            .Where(trade => trade.NormalizedSymbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static string CreateTradeId(string normalizedSymbol, DateTimeOffset now)
    {
        string value = $"TL-{now:yyyyMMddHHmmssfff}-{normalizedSymbol}-{Guid.NewGuid():N}";
        return value.Length <= 64 ? value : value[..64];
    }

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();

    private static string NormalizeProfile(string profileName)
        => string.IsNullOrWhiteSpace(profileName) ? "unknown" : profileName.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
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
