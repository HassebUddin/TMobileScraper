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
        string[] exportColumns = ["Product Name", "SKU", "Price"];

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

            var scrapingResult = await PlaywrightScraperHelper.ScrapeTMobileDealerOrderingAsync(website, catalogFilter, _options, cancellationToken);
            if (!scrapingResult.Success)
                return new ScrapingExportResult { Success = false, Message = scrapingResult.Message };

            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var stream = ExcelExportHelper.BuildMultiSheetWorkbook(scrapingResult.Data, exportColumns);
            files[sourceType.ToString()] = stream.ToArray();

            var totalProducts = scrapingResult.Data.Values.Sum(static rows => rows.Count);
            var message = $"{totalProducts} products exported in 1 file.";
            _logger.LogInformation("{LogKey} | Website={Website} | Result=Success | Message={Message}", logKey, website.website_url, message);
            return new ScrapingExportResult { Success = true, Message = message, Files = files };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogKey} | Result=Failed | Error={Error}", logKey, ex.Message);
            return new ScrapingExportResult { Success = false, Message = "Unable to export the catalog right now. Please try again." };
        }
    }
}
