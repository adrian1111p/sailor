using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListWorkbookReader
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly Regex CellReferenceRegex = new("^([A-Za-z]+)([0-9]+)$", RegexOptions.Compiled);

    public Task<ScanListWorkbookResult> ReadAsync(
        ScanListWorkbookOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(options));
    }

    public ScanListWorkbookResult Read(ScanListWorkbookOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            throw new ArgumentException("Scan-list workbook path is required.", nameof(options));
        }

        string workbookPath = Path.GetFullPath(options.FilePath);
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException($"Scan-list workbook was not found: {options.FilePath}", options.FilePath);
        }

        using ZipArchive archive = ZipFile.OpenRead(workbookPath);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        string worksheetEntryName = ResolveWorksheetEntryName(archive, options.SheetName);
        ZipArchiveEntry worksheetEntry = archive.GetEntry(worksheetEntryName)
            ?? throw new InvalidOperationException($"Worksheet entry was not found in workbook: {worksheetEntryName}");

        using Stream worksheetStream = worksheetEntry.Open();
        XDocument worksheetDocument = XDocument.Load(worksheetStream);

        string targetColumn = NormalizeColumnName(options.SymbolColumn);
        var symbols = new List<string>();
        var rejected = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement cell in worksheetDocument.Descendants(SpreadsheetNs + "c"))
        {
            string reference = cell.Attribute("r")?.Value ?? string.Empty;
            string column = ExtractColumnName(reference);
            if (!string.Equals(column, targetColumn, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string raw = ReadCellValue(cell, sharedStrings);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (string token in raw.Split([',', ';', '\t', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string symbol = NormalizeSymbol(token);
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    string trimmed = token.Trim();
                    if (!IsHeader(trimmed) && trimmed.Length > 0)
                    {
                        rejected.Add(trimmed);
                    }

                    continue;
                }

                if (seen.Add(symbol))
                {
                    symbols.Add(symbol);
                }
            }
        }

        if (symbols.Count == 0)
        {
            warnings.Add($"No symbols were read from sheet '{options.SheetName}' column '{options.SymbolColumn}'.");
        }

        return new ScanListWorkbookResult(
            options,
            symbols,
            rejected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings,
            DateTimeOffset.UtcNow);
    }

    public static string NormalizeSymbol(string token)
    {
        string value = token.Trim().Trim('"', '\'', '\uFEFF').ToUpperInvariant();
        if (IsHeader(value))
        {
            return string.Empty;
        }

        int pipe = value.IndexOf('|', StringComparison.Ordinal);
        if (pipe > 0)
        {
            value = value[..pipe];
        }

        int colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0 && colon < value.Length - 1)
        {
            value = value[(colon + 1)..];
        }

        value = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-').ToArray());
        if (value.Length is < 1 or > 12)
        {
            return string.Empty;
        }

        return value;
    }

    private static bool IsHeader(string value)
        => value.Equals("SYMBOL", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("TICKER", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("TICKERS", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("CANDIDATE", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("CANDIDATES", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ResolveWorksheetEntryName(ZipArchive archive, string requestedSheetName)
    {
        ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("The workbook is missing xl/workbook.xml.");
        ZipArchiveEntry relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("The workbook is missing xl/_rels/workbook.xml.rels.");

        XDocument workbookDocument;
        using (Stream workbookStream = workbookEntry.Open())
        {
            workbookDocument = XDocument.Load(workbookStream);
        }

        XDocument relsDocument;
        using (Stream relsStream = relsEntry.Open())
        {
            relsDocument = XDocument.Load(relsStream);
        }

        IReadOnlyDictionary<string, string> relTargets = relsDocument
            .Descendants(RelationshipNs + "Relationship")
            .Where(rel => rel.Attribute("Id") is not null && rel.Attribute("Target") is not null)
            .ToDictionary(
                rel => rel.Attribute("Id")!.Value,
                rel => NormalizeWorkbookRelationshipTarget(rel.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase);

        XElement? selectedSheet = workbookDocument
            .Descendants(SpreadsheetNs + "sheet")
            .FirstOrDefault(sheet => string.Equals(sheet.Attribute("name")?.Value, requestedSheetName, StringComparison.OrdinalIgnoreCase));

        if (selectedSheet is null)
        {
            string available = string.Join(", ", workbookDocument.Descendants(SpreadsheetNs + "sheet").Select(sheet => sheet.Attribute("name")?.Value).Where(name => !string.IsNullOrWhiteSpace(name)));
            throw new InvalidOperationException($"Sheet '{requestedSheetName}' was not found. Available sheets: {available}");
        }

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string relationshipId = selectedSheet.Attribute(relNs + "id")?.Value
            ?? throw new InvalidOperationException($"Sheet '{requestedSheetName}' does not have a relationship id.");

        if (!relTargets.TryGetValue(relationshipId, out string? worksheetEntryName))
        {
            throw new InvalidOperationException($"No worksheet relationship target found for sheet '{requestedSheetName}' ({relationshipId}).");
        }

        return worksheetEntryName;
    }

    private static string NormalizeWorkbookRelationshipTarget(string target)
    {
        string normalized = target.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("../", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        return "xl/" + normalized;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        string type = cell.Attribute("t")?.Value ?? string.Empty;
        string value = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;

        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex) &&
            sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        }

        return value;
    }

    private static string NormalizeColumnName(string column)
    {
        string value = string.IsNullOrWhiteSpace(column)
            ? ScanListWorkbookOptions.DefaultSymbolColumn
            : column.Trim().ToUpperInvariant();

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int columnIndex) && columnIndex > 0)
        {
            return ColumnNumberToName(columnIndex);
        }

        return new string(value.Where(char.IsLetter).ToArray());
    }

    private static string ExtractColumnName(string cellReference)
    {
        Match match = CellReferenceRegex.Match(cellReference);
        return match.Success
            ? match.Groups[1].Value.ToUpperInvariant()
            : string.Empty;
    }

    private static string ColumnNumberToName(int columnIndex)
    {
        var chars = new Stack<char>();
        int value = columnIndex;
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        }

        return new string(chars.ToArray());
    }
}
