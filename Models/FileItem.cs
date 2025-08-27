using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drop1.Api.Models;

[Table("Files")]
public class FileItem
{
    [Key] public int FileID { get; set; }
    [Required, MaxLength(255)] public string FileName { get; set; } = string.Empty;
    public decimal FileSizeMB { get; set; }
    [MaxLength(50)] public string? FileType { get; set; }
    public int? FolderID { get; set; }
    public long UserID { get; set; }
    public DateTime UploadedAt { get; set; }
    [Required, MaxLength(500)] public string FilePath { get; set; } = string.Empty;
    public bool IsDeleted { get; set; } = false;
}
