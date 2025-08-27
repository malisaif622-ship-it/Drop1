// Models/Folder.cs
using System.ComponentModel.DataAnnotations;

namespace Drop1.Models
{
    public class Folder
    {
        public int FolderID { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public int? ParentFolderID { get; set; }
        public long UserID { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsDeleted { get; set; } = false;
        [Required, MaxLength(500)] public string FolderPath { get; set; } = string.Empty;
    }
}
