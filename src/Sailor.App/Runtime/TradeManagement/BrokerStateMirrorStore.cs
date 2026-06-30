using System.Text.Json;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.TradeManagement;

public sealed class BrokerStateMirrorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;

    public BrokerStateMirrorStore(SailorRuntimeMode mode)
    {
        _mode = mode;
        MirrorDirectory = EnsureDirectory(Path.Combine(RepositoryRoot, "state", mode.ToDisplayName(), "broker-mirror"));
        LatestJsonPath = Path.Combine(MirrorDirectory, "broker_state_mirror_latest.json");
        DailyJsonlPath = Path.Combine(MirrorDirectory, $"broker_state_mirror_{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public string MirrorDirectory { get; }

    public string LatestJsonPath { get; }

    public string DailyJsonlPath { get; }

    public BrokerStateMirrorSnapshot Save(BrokerStateMirrorSnapshot snapshot)
    {
        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.AppendAllText(DailyJsonlPath, JsonSerializer.Serialize(snapshot, JsonOptions).Replace(Environment.NewLine, string.Empty) + Environment.NewLine);
        return snapshot;
    }

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
