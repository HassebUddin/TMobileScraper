using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMobileScraper.Models;

[Table("email_members")]
public class EmailMember
{
    [Key]
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
}

[Table("email_member_details")]
public class EmailMemberDetail
{
    [Key]
    public int id { get; set; }
    public int member_id { get; set; }
    public int email_type_id { get; set; }
    public int recipient_type_id { get; set; }
}
