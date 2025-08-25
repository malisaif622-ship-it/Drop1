// Models/Folder.cs
namespace Drop1.Models
{
    public class Folder
    {
        public int FolderID { get; set; }
        public string FolderName { get; set; }
        public int? ParentFolderID { get; set; }
        public int UserID { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
