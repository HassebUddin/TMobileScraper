using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TMobileScraper.Data;
using TMobileScraper.Dto;
using TMobileScraper.Interfaces;

namespace TMobileScraper.Repositories;

public sealed class EmailRepository : IEmailRepository
{
    private readonly LeasingDbContext _db;
    private readonly ILogger<EmailRepository> _logger;

    public EmailRepository(LeasingDbContext db, ILogger<EmailRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<EmailRecipientDto>> GetRecipientsAsync(int emailTypeId)
    {
        try
        {
            const string sql = """
                SELECT m.name AS Name, m.email AS Email, d.recipient_type_id AS RecipientTypeId
                FROM email_member_details d
                INNER JOIN email_members m ON m.id = d.member_id
                WHERE d.email_type_id = {0}
                """;

            return await _db.Database.SqlQueryRaw<EmailRecipientDto>(sql, emailTypeId).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRecipientsAsync failed | EmailTypeId={EmailTypeId}", emailTypeId);
            throw;
        }
    }
}
