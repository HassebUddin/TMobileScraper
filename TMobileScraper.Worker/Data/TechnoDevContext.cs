using Microsoft.EntityFrameworkCore;
using TMobileScraper.Models;

namespace TMobileScraper.Data;

public sealed class TechnoDevContext : DbContext
{
    public TechnoDevContext(DbContextOptions<TechnoDevContext> options) : base(options) { }
    public DbSet<ScrapingWebsite> ScrapingWebsites => Set<ScrapingWebsite>();
}
