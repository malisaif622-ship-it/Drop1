using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drop1.Api.Models;

[Table("Users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long UserID { get; set; }
    [Required, MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [MaxLength(100)] public string? Department { get; set; }
    public int TotalStorageMB { get; set; } = 200;
    public decimal UsedStorageMB { get; set; } = 0;
}
