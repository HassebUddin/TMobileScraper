using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TMobileScraper.Data;
using TMobileScraper.Interfaces;
using TMobileScraper.Models;

namespace TMobileScraper.Repositories;

public sealed class ScrapingWebsiteRepository : IScrapingWebsiteRepository
{
    private readonly TechnoDevContext _db;
    private readonly ILogger<ScrapingWebsiteRepository> _logger;

    public ScrapingWebsiteRepository(TechnoDevContext db, ILogger<ScrapingWebsiteRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ScrapingWebsite?> GetActiveBySourceTypeIdAsync(int sourceTypeId)
    {
        try
        {
            return await _db.ScrapingWebsites.AsNoTracking()
                .FirstOrDefaultAsync(x => x.is_active && x.source_type_id == sourceTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetActiveBySourceTypeIdAsync failed | SourceTypeId={SourceTypeId}", sourceTypeId);
            throw;
        }
    }
}
