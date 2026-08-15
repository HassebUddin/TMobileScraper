using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TMobileScraper.Dto;
using TMobileScraper.Enums;
using TMobileScraper.Helpers;
using TMobileScraper.Interfaces;
using TMobileScraper.Options;

namespace TMobileScraper.Services;

public sealed class ScrapingWebsiteService : IScrapingWebsiteService
{
    private readonly IScrapingWebsiteRepository _scrapingWebsiteRepository;
    private readonly ScrapingOptions _options;
    private readonly ILogger<ScrapingWebsiteService> _logger;

    public ScrapingWebsiteService(
        IScrapingWebsiteRepository scrapingWebsiteRepository,
        IOptions<ScrapingOptions> options,
        ILogger<ScrapingWebsiteService> logger)
    {
        _scrapingWebsiteRepository = scrapingWebsiteRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ScrapingExportResult> ExportTMobileCatalogAsync(ScrapingSourceType sourceType, CancellationToken cancellationToken = default)
    {
        string logKey = $"ExportTMobileCatalogAsync_{DateTime.Now:yyyy-MM-dd HH:mm:ss} | SourceType={sourceType}";
        const string exportName = "TMobileCatalogScraping";
        string[] exportColumns = ["Product Name", "SKU", "Price", "Date", "Time"];

        try
        {
            var website = await _scrapingWebsiteRepository.GetActiveBySourceTypeIdAsync((int)sourceType);
            if (website is null)
                return new ScrapingExportResult { Success = false, Message = $"No active website found for sourceTypeId '{(int)sourceType}'." };
            if (_options.TimeoutSeconds <= 0)
                return new ScrapingExportResult { Success = false, Message = "Scraping timeout must be greater than zero." };
            if (string.IsNullOrWhiteSpace(website.filter))
                return new ScrapingExportResult { Success = false, Message = "Website filter is not configured in the database." };

            var catalogFilter = website.filter.Trim();
            logKey += $" | Filter={catalogFilter}";

            _logger.LogInformation("{LogKey} | Scraping started | Website={Website}", logKey, website.website_url);

            var scrapingResult = await PlaywrightScraperHelper.ScrapeTMobileDealerOrderingAsync(website, catalogFilter, _options, cancellationToken);
            if (!scrapingResult.Success)
                return new ScrapingExportResult { Success = false, Message = scrapingResult.Message };

            var scrapedAt = DateTime.Now;
            var allRows = new List<Dictionary<string, object?>>();

            foreach (var rows in scrapingResult.Data.Values)
            {
                foreach (var row in rows)
                {
                    allRows.Add(new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                    {
                        ["Date"] = scrapedAt.Date,                        
                        ["Time"] = scrapedAt.ToString("HH:mm:ss")        
                    });
                }
            }

            var totalProducts = allRows.Count;
            var message = $"{totalProducts} products exported in 1 file.";
            _logger.LogInformation("{LogKey} | Website={Website} | Result=Success | Message={Message}", logKey, website.website_url, message);
            return new ScrapingExportResult
            {
                Success = true,
                Message = message,
                ExportBaseName = exportName,
                Columns = exportColumns,
                Sheets = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    [exportName] = allRows
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogKey} | Result=Failed | Error={Error}", logKey, ex.Message);
            return new ScrapingExportResult { Success = false, Message = "Unable to export the catalog right now. Please try again." };
        }
    }
}
