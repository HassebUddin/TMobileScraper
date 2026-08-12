using TMobileScraper.Enums;

namespace TMobileScraper.Dto;

public class EmailRecipientDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int RecipientTypeId { get; set; } = (int)RecipientType.To;
}
