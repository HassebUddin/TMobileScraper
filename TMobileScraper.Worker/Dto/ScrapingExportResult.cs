namespace TMobileScraper.Dto;

public sealed class ScrapingExportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public Dictionary<string, byte[]> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
