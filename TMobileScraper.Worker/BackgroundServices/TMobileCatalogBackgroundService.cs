using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TMobileScraper.Enums;
using TMobileScraper.Interfaces;
using TMobileScraper.Options;
using TMobileScraper.Templates.Email;

namespace TMobileScraper.BackgroundServices;

public sealed class TMobileCatalogBackgroundService
{
    private static readonly Regex InvalidFileChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

    private readonly IScrapingWebsiteService _scrapingWebsiteService;
    private readonly IEmailService _emailService;
    private readonly ScrapingOptions _options;
    private readonly ILogger<TMobileCatalogBackgroundService> _logger;

    public TMobileCatalogBackgroundService(IScrapingWebsiteService scrapingWebsiteService, IEmailService emailService, IOptions<ScrapingOptions> options, ILogger<TMobileCatalogBackgroundService> logger)
    {
        _scrapingWebsiteService = scrapingWebsiteService;
        _emailService = emailService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var logKey = $"TMobileCatalogBackgroundService_{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        try
        {
            _logger.LogInformation("{LogKey} | Started | OutputFolder={Folder}", logKey, _options.OutputFolder);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.TimeoutSeconds, 60)));

            var result = await _scrapingWebsiteService.ExportTMobileCatalogAsync(ScrapingSourceType.TMobileDealerOrdering, cts.Token);
            if (!result.Success)
            {
                _logger.LogError("{LogKey} | Scrape failed | {Message}", logKey, result.Message);
                var failBody = EmailTemplateBuilder.CreateEmailBody("T-Mobile Catalog Export", new Dictionary<string, string> { ["Result"] = result.Message });
                await _emailService.SendEmailAsync(EmailType.TMobileCatalogExport, "TechnoComm Scraping", "T-Mobile Catalog Export", failBody);
                return 1;
            }

            var today = DateTime.Today;
            var attachments = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var saveErrors = new List<string>();

            try
            {
                Directory.CreateDirectory(_options.OutputFolder);
            }
            catch (Exception ex)
            {
                saveErrors.Add($"Could not create folder '{_options.OutputFolder}': {ex.Message}");
                _logger.LogWarning(ex, "{LogKey} | Save folder failed", logKey);
            }

            foreach (var file in result.Files)
            {
                var safeName = InvalidFileChars.Replace(file.Key.Trim(), "_");
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "TMobileDealerOrdering";

                var fileName = $"{safeName}_{today:yyyyMMdd}.xlsx";
                attachments[fileName] = file.Value;

                try
                {
                    var fullPath = Path.Combine(_options.OutputFolder, fileName);
                    await File.WriteAllBytesAsync(fullPath, file.Value, cancellationToken);
                    _logger.LogInformation("{LogKey} | Saved | {Path}", logKey, fullPath);
                }
                catch (Exception ex)
                {
                    saveErrors.Add($"Could not save '{fileName}': {ex.Message}");
                    _logger.LogWarning(ex, "{LogKey} | Save file failed | {FileName}", logKey, fileName);
                }
            }

            var details = new Dictionary<string, string>
            {
                ["Result"] = result.Message,
                ["File"] = string.Join(", ", attachments.Keys)
            };
            if (saveErrors.Count > 0)
                details["Save errors"] = string.Join(" | ", saveErrors);

            var htmlBody = EmailTemplateBuilder.CreateEmailBody("T-Mobile Catalog Export", details);
            var emailSent = await _emailService.SendEmailWithAttachmentsAsync(
                EmailType.TMobileCatalogExport,
                "TechnoComm Scraping",
                "T-Mobile Catalog Export",
                htmlBody,
                attachments);

            _logger.LogInformation("{LogKey} | EmailSent={EmailSent} | Result=Completed", logKey, emailSent);
            return saveErrors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogKey} | Failed | {Error}", logKey, ex.Message);
            var errorBody = EmailTemplateBuilder.CreateEmailBody("T-Mobile Catalog Export", new Dictionary<string, string> { ["Error"] = ex.Message });
            await _emailService.SendEmailAsync(EmailType.TMobileCatalogExport, "TechnoComm Scraping", "T-Mobile Catalog Export", errorBody);
            return 1;
        }
    }
}
