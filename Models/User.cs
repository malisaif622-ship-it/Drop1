using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drop1.Api.Models;

[Table("Users")]
public class User
{
    [Key] public int UserID { get; set; }
    [Required, MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [MaxLength(100)] public string? Department { get; set; }
    public int TotalStorageMB { get; set; }
    public int UsedStorageMB { get; set; }
}
