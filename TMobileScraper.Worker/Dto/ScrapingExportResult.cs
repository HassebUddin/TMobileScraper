namespace TMobileScraper.Dto;

public sealed class ScrapingExportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string ExportBaseName { get; init; } = "";
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyDictionary<string, List<Dictionary<string, object?>>> Sheets { get; init; }
        = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
}
