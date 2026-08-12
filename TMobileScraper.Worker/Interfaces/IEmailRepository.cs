using TMobileScraper.Dto;

namespace TMobileScraper.Interfaces;

public interface IEmailRepository
{
    Task<List<EmailRecipientDto>> GetRecipientsAsync(int emailTypeId);
}
