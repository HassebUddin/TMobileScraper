using Microsoft.EntityFrameworkCore;
using TMobileScraper.Models;

namespace TMobileScraper.Data;

public sealed class LeasingDbContext : DbContext
{
    public LeasingDbContext(DbContextOptions<LeasingDbContext> options) : base(options) { }

    public DbSet<EmailMember> EmailMembers => Set<EmailMember>();
    public DbSet<EmailMemberDetail> EmailMemberDetails => Set<EmailMemberDetail>();
}
