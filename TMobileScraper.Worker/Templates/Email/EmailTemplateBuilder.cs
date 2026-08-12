using System.Net;

namespace TMobileScraper.Templates.Email;

public class EmailBodyChange
{
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
}

public static class EmailTemplateBuilder
{
    public static string CreateEmailBody(string title, Dictionary<string, string> details, IEnumerable<EmailBodyChange>? changes = null)
    {
        var detailsHtml = string.Join("", details.Select(detail =>
            $"<p><strong>{WebUtility.HtmlEncode(detail.Key)}:</strong> {WebUtility.HtmlEncode(detail.Value)}</p>"));

        string changesHtml = string.Empty;
        if (changes != null && changes.Any())
        {
            var rows = string.Join("", changes.Select(change => $@"
                <tr>
                    <td style='padding:8px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(change.Field)}</td>
                    <td style='padding:8px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(change.OldValue)}</td>
                    <td style='padding:8px;border:1px solid #ddd;'>{WebUtility.HtmlEncode(change.NewValue)}</td>
                </tr>"));

            changesHtml = $@"
                <table cellpadding='0' cellspacing='0' style='border-collapse:collapse;'>
                    <tr style='background-color:#f2f2f2;'>
                        <th style='padding:8px;border:1px solid #ddd;'>Field</th>
                        <th style='padding:8px;border:1px solid #ddd;'>Old Value</th>
                        <th style='padding:8px;border:1px solid #ddd;'>New Value</th>
                    </tr>
                    {rows}
                </table>";
        }

        return $@"
            <html>
            <body style='font-family:Arial,sans-serif;'>
                <h2>{WebUtility.HtmlEncode(title)}</h2>
                {detailsHtml}
                {changesHtml}
            </body>
            </html>";
    }
}
