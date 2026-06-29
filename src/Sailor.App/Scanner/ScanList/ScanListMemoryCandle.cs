namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListMemoryCandle(
    string Symbol,
    DateTimeOffset MinuteUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    ScanListCandleQuality Quality,
    DateTimeOffset UpdatedUtc)
{
    public string ToDisplayLine()
        => $"{Symbol} {MinuteUtc:O} O={Open:0.####} H={High:0.####} L={Low:0.####} C={Close:0.####} V={Volume} quality={Quality}";
}
