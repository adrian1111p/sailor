namespace Sailor.App.Runtime.Ui;

public static class SailorUiStrategyProfileNames
{
    public static string Normalize(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return string.Empty;
        }

        string normalized = strategy.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "sailor-trendvolume" => "sailor-trend-volume",
            "sailor-trend-volume" => "sailor-trend-volume",
            "sailor-conductv3" => "sailor-conduct-v3",
            "sailor-conduct-v3" => "sailor-conduct-v3",
            "simplemomentum" => "simple-momentum",
            "simple-momentum" => "simple-momentum",
            "harvester-conductv3" => "harvester-conduct-v3",
            "harvester-conduct-v3" => "harvester-conduct-v3",
            "harvester-conductv9" => "harvester-conduct-v9",
            "harvester-conduct-v9" => "harvester-conduct-v9",
            "v21" => "v21-15minutes",
            "v21-15minutes" => "v21-15minutes",
            "v22" => "v22-15minutes",
            "v22-15minutes" => "v22-15minutes",
            "v23" => "v23-5minutes",
            "v23-5minutes" => "v23-5minutes",
            "v24" => "v24-5minutes",
            "v24-5minutes" => "v24-5minutes",
            "v20" => "v20-gen001-choppyshield",
            "v20-gen001-choppyshield" => "v20-gen001-choppyshield",
            "v19" => "v19-purplecloud",
            "v19-purplecloud" => "v19-purplecloud",
            "v18" => "v18-silver",
            "v18-silver" => "v18-silver",
            "v17" => "v17-hybridflow",
            "v17-hybridflow" => "v17-hybridflow",
            "v16" => "v16-sqzbreakout",
            "v16-sqzbreakout" => "v16-sqzbreakout",
            "v15" => "v15-shortcap",
            "v15-shortcap" => "v15-shortcap",
            "v14" => "v14-smallcap",
            "v14-smallcap" => "v14-smallcap",
            "v13" => "v13",
            "v12" => "v12",
            "v10" => "v10-hybrid",
            "v10-hybrid" => "v10-hybrid",
            "v2" => "v2-conduct",
            "v2-conduct" => "v2-conduct",
            "v1" => "v1-first",
            "v1-first" => "v1-first",
            "conduct-v3" => "conduct-v3",
            _ => normalized
        };
    }
}
