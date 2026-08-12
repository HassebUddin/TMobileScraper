using TMobileScraper.Models;

namespace TMobileScraper.Interfaces;

public interface IScrapingWebsiteRepository
{
    Task<ScrapingWebsite?> GetActiveBySourceTypeIdAsync(int sourceTypeId);
}
