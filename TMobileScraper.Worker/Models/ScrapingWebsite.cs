using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMobileScraper.Models;

[Table("scraping_websites")]
public class ScrapingWebsite
{
    [Key]
    public int id { get; set; }

    public int source_type_id { get; set; }

    [Required]
    [MaxLength(100)]
    public string name { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string website_url { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string username { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string password { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string filter { get; set; } = "";

    public bool is_active { get; set; }
}
