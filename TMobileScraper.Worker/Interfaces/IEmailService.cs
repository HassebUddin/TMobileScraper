using TMobileScraper.Dto;
using TMobileScraper.Enums;

namespace TMobileScraper.Interfaces;

public interface IEmailService
{
    Task<bool> SendEmailAsync(EmailType emailType, string fromName, string subject, string htmlBody);

    Task<bool> SendEmailWithAttachmentsAsync(EmailType emailType, string fromName, string subject, string htmlBody, IReadOnlyDictionary<string, byte[]> attachments);

    Task<bool> SendEmailToRecipientsAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients);

    Task<bool> SendEmailWithAttachmentsToRecipientsAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients, IReadOnlyDictionary<string, byte[]> attachments);
}
