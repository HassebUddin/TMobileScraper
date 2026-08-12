using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using TMobileScraper.Dto;
using TMobileScraper.Enums;
using TMobileScraper.Interfaces;

namespace TMobileScraper.Services;

public sealed class EmailService : IEmailService
{
    private readonly IEmailRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IEmailRepository repository, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(EmailType emailType, string fromName, string subject, string htmlBody)
    {
        var recipients = await _repository.GetRecipientsAsync((int)emailType);
        return await SendAsync(fromName, subject, htmlBody, recipients);
    }

    public async Task<bool> SendEmailWithAttachmentsAsync(EmailType emailType, string fromName, string subject, string htmlBody, IReadOnlyDictionary<string, byte[]> attachments)
    {
        var recipients = await _repository.GetRecipientsAsync((int)emailType);
        return await SendWithAttachmentsAsync(fromName, subject, htmlBody, recipients, attachments);
    }

    public Task<bool> SendEmailToRecipientsAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients)
    {
        return SendAsync(fromName, subject, htmlBody, recipients ?? new List<EmailRecipientDto>());
    }

    public Task<bool> SendEmailWithAttachmentsToRecipientsAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients, IReadOnlyDictionary<string, byte[]> attachments)
    {
        return SendWithAttachmentsAsync(fromName, subject, htmlBody, recipients ?? new List<EmailRecipientDto>(), attachments);
    }

    private async Task<bool> SendAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients)
    {
        string logKey = $"SendEmail_{DateTime.Now:yyyy-MM-dd_HH:mm:ss}";
        Exception? sendException = null;
        bool sent = false;

        try
        {
            if (recipients == null || recipients.Count == 0)
            {
                logKey += $" | Result=No recipients | Subject={subject}";
                return false;
            }

            var host = _configuration["Smtp:Host"];
            int.TryParse(_configuration["Smtp:Port"], out int port);
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"] ?? username;

            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fromEmail))
            {
                logKey += " | Result=SMTP settings missing";
                return false;
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(fromName, fromEmail));
            email.Subject = subject;
            email.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
            foreach (var recipient in recipients)
            {
                var address = new MailboxAddress(recipient.Name, recipient.Email);
                switch ((RecipientType)recipient.RecipientTypeId)
                {
                    case RecipientType.To: email.To.Add(address); break;
                    case RecipientType.Cc: email.Cc.Add(address); break;
                    case RecipientType.Bcc: email.Bcc.Add(address); break;
                }
            }

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            sent = true;
            logKey += $" | Result=Sent | Subject={subject} | Recipients={recipients.Count}";
            return true;
        }
        catch (Exception exception)
        {
            sendException = exception;
            logKey += $" | Result=Failed | Subject={subject} | Error={exception.Message}";
            return false;
        }
        finally
        {
            if (sendException != null) _logger.LogError(sendException, "{EmailLog}", logKey);
            else if (sent) _logger.LogInformation("{EmailLog}", logKey);
            else _logger.LogWarning("{EmailLog}", logKey);
        }
    }

    private async Task<bool> SendWithAttachmentsAsync(string fromName, string subject, string htmlBody, List<EmailRecipientDto> recipients, IReadOnlyDictionary<string, byte[]> attachments)
    {
        string logKey = $"SendEmailWithAttachments_{DateTime.Now:yyyy-MM-dd_HH:mm:ss}";
        Exception? sendException = null;
        bool sent = false;

        try
        {
            if (recipients == null || recipients.Count == 0)
            {
                logKey += $" | Result=No recipients | Subject={subject}";
                return false;
            }

            var host = _configuration["Smtp:Host"];
            int.TryParse(_configuration["Smtp:Port"], out int port);
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"] ?? username;

            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fromEmail))
            {
                logKey += " | Result=SMTP settings missing";
                return false;
            }

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            foreach (var attachment in attachments)
            {
                bodyBuilder.Attachments.Add(
                    attachment.Key,
                    attachment.Value,
                    new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(fromName, fromEmail));
            email.Subject = subject;
            email.Body = bodyBuilder.ToMessageBody();
            foreach (var recipient in recipients)
            {
                var address = new MailboxAddress(recipient.Name, recipient.Email);
                switch ((RecipientType)recipient.RecipientTypeId)
                {
                    case RecipientType.To: email.To.Add(address); break;
                    case RecipientType.Cc: email.Cc.Add(address); break;
                    case RecipientType.Bcc: email.Bcc.Add(address); break;
                }
            }

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            sent = true;
            logKey += $" | Result=Sent | Subject={subject} | Recipients={recipients.Count} | Attachments={attachments.Count}";
            return true;
        }
        catch (Exception exception)
        {
            sendException = exception;
            logKey += $" | Result=Failed | Subject={subject} | Error={exception.Message}";
            return false;
        }
        finally
        {
            if (sendException != null) _logger.LogError(sendException, "{EmailLog}", logKey);
            else if (sent) _logger.LogInformation("{EmailLog}", logKey);
            else _logger.LogWarning("{EmailLog}", logKey);
        }
    }
}
