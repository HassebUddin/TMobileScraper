using TMobileScraper.Dto;
using TMobileScraper.Enums;

namespace TMobileScraper.Interfaces;

public interface IScrapingWebsiteService
{
    Task<ScrapingExportResult> ExportTMobileCatalogAsync(ScrapingSourceType sourceType, CancellationToken cancellationToken = default);
}
