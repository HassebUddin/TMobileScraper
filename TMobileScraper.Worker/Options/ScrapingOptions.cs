namespace TMobileScraper.Options;

public sealed class ScrapingOptions
{
    public const string SectionName = "Scraping";

    public bool Headless { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public int SlowMo { get; set; }
    public int DebugPauseSeconds { get; set; } = 30;
    public string OutputFolder { get; set; } = @"\\192.168.1.3\Bot_Data_IT";
}
