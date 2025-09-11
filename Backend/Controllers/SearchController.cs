using Drop1.Api.Data;
using Drop1.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Drop1.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public SearchController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // =========================
        // SEARCH FILES + FOLDERS (DB + Physical Drive Check)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string keyword, [FromQuery] int? parentFolderId = null, [FromQuery] bool deletedOnly = false)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            // Normalize keyword
            keyword = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                return Ok(new { Keyword = "", Folders = new List<object>(), Files = new List<object>() });

            var lowered = keyword.ToLowerInvariant();
            var rootPath = _config["StorageSettings:RootPath"] ?? @"C:\Drop1";

            // Get all folders that match the search criteria
            var allFolders = await _context.Folders
                .Where(f => f.UserID == userId && f.IsDeleted == deletedOnly &&
                           f.FolderName.ToLower().Contains(lowered))
                .ToListAsync();

            List<Drop1.Models.Folder> dbFolders;

            if (deletedOnly)
            {
                // In recycle bin, show all deleted folders regardless of hierarchy
                dbFolders = allFolders;
            }
            else if (parentFolderId.HasValue)
            {
                // In a specific folder: show folders that are direct children or descendants of this folder
                var descendantFolderIds = GetDescendantFolderIds(allFolders, parentFolderId.Value);
                // Also include direct children
                var directChildren = allFolders.Where(f => f.ParentFolderID == parentFolderId.Value).Select(f => f.FolderID).ToList();
                descendantFolderIds.AddRange(directChildren);
                dbFolders = allFolders.Where(f => descendantFolderIds.Contains(f.FolderID)).ToList();
            }
            else
            {
                // In root: show all non-deleted folders (entire hierarchy)
                dbFolders = allFolders;
            }

            // Fix path checking - skip path verification for deleted items
            var folders = dbFolders
                .Where(f => {
                    if (deletedOnly) return true; // Don't check paths for deleted items
                    var candidate = Path.IsPathRooted(f.FolderPath) ? f.FolderPath : Path.Combine(rootPath, f.FolderPath);
                    return System.IO.Directory.Exists(candidate);
                })
                .Select(f => new
                {
                    FolderID = f.FolderID,
                    f.FolderName,
                    f.ParentFolderID,
                    f.CreatedAt
                })
                .ToList();

            // Get all files that match the search criteria
            var allFiles = await _context.Files
                .Where(f => f.UserID == userId && f.IsDeleted == deletedOnly &&
                          (f.FileName.ToLower().Contains(lowered) ||
                           (f.FileType ?? "").ToLower().Contains(lowered)))
                .ToListAsync();

            List<FileItem> dbFiles;

            if (deletedOnly)
            {
                // In recycle bin, show all deleted files regardless of original location
                dbFiles = allFiles;
            }
            else if (parentFolderId.HasValue)
            {
                // In a specific folder: show files in this folder and all its descendant folders
                var descendantFolderIds = GetDescendantFolderIds(await _context.Folders
                    .Where(f => f.UserID == userId && !f.IsDeleted).ToListAsync(), parentFolderId.Value);
                descendantFolderIds.Add(parentFolderId.Value); // Include the parent folder itself

                dbFiles = allFiles.Where(f => descendantFolderIds.Contains(f.FolderID ?? 0)).ToList();
            }
            else
            {
                // In root: show all non-deleted files (entire hierarchy)
                dbFiles = allFiles;
            }

            // Fix path checking for files too - skip path verification for deleted items
            var files = dbFiles
                .Where(f => {
                    if (deletedOnly) return true; // Don't check paths for deleted items
                    var candidate = Path.IsPathRooted(f.FilePath) ? f.FilePath : Path.Combine(rootPath, f.FilePath);
                    return System.IO.File.Exists(candidate);
                })
                .Select(f => new
                {
                    FileID = f.FileID,
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,
                    f.FolderID,
                    f.UploadedAt
                })
                .ToList();

            return Ok(new { Keyword = keyword, Folders = folders, Files = files });
        }

        // =========================
        // GET DELETED ITEMS
        // =========================
        [HttpGet("deleted")]
        public async Task<IActionResult> GetDeletedItems()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            var deletedFolders = await _context.Folders
                .Where(f => f.UserID == userId && f.IsDeleted)
                .Select(f => new
                {
                    f.FolderID,
                    f.FolderName,
                    f.CreatedAt,
                    Type = "folder"
                })
                .ToListAsync();

            var deletedFiles = await _context.Files
                .Where(f => f.UserID == userId && f.IsDeleted)
                .Select(f => new
                {
                    f.FileID,
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,
                    f.UploadedAt,
                    Type = "file"
                })
                .ToListAsync();

            return Ok(new { Folders = deletedFolders, Files = deletedFiles });
        }

        // =========================
        // GET ALL USER ITEMS (unscoped)
        // =========================
        [HttpGet("all")]
        public async Task<IActionResult> GetAllUserItems()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            var folders = await _context.Folders
                .Where(f => f.UserID == userId && !f.IsDeleted)
                .Select(f => new
                {
                    FolderID = f.FolderID,
                    f.FolderName,
                    f.ParentFolderID,
                    f.CreatedAt,
                    Type = "folder"
                })
                .ToListAsync();

            var files = await _context.Files
                .Where(f => f.UserID == userId && !f.IsDeleted)
                .Select(f => new
                {
                    FileID = f.FileID,
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,
                    f.FolderID,
                    f.UploadedAt,
                    Type = "file"
                })
                .ToListAsync();

            return Ok(new { Folders = folders, Files = files });
        }

        // =========================
        // GET USER FOLDERS + FILES (Scoped by parentFolderId)
        // =========================
        [HttpGet("list")]
        public async Task<IActionResult> GetUserFoldersAndFiles([FromQuery] int? parentFolderId = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            var folders = await _context.Folders
                .Where(f => f.UserID == userId && f.ParentFolderID == parentFolderId && !f.IsDeleted)
                .Select(f => new
                {
                    FolderID = f.FolderID,
                    f.FolderName,
                    f.ParentFolderID,
                    f.CreatedAt
                })
                .ToListAsync();

            var files = await _context.Files
                .Where(file => file.UserID == userId && file.FolderID == parentFolderId && !file.IsDeleted)
                .Select(file => new
                {
                    FileID = file.FileID,
                    file.FileName,
                    file.FileSizeMB,
                    file.FileType,
                    file.FolderID,
                    file.UploadedAt
                })
                .ToListAsync();

            return Ok(new { Folders = folders, Files = files });
        }

        // =========================
        // GET CONTEXTUAL LIST (for empty searches or folder browsing)
        // =========================
        [HttpGet("contextual-list")]
        public async Task<IActionResult> GetContextualList([FromQuery] int? parentFolderId = null, [FromQuery] bool deletedOnly = false)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            // Get folders based on context
            var foldersQuery = _context.Folders
                .Where(f => f.UserID == userId && f.IsDeleted == deletedOnly);

            if (parentFolderId.HasValue)
            {
                foldersQuery = foldersQuery.Where(f => f.ParentFolderID == parentFolderId.Value);
            }
            else
            {
                foldersQuery = foldersQuery.Where(f => f.ParentFolderID == null);
            }

            var folders = await foldersQuery
                .Select(f => new
                {
                    FolderID = f.FolderID,
                    f.FolderName,
                    f.ParentFolderID,
                    f.CreatedAt,
                    Type = "folder"
                })
                .ToListAsync();

            // Get files based on context
            var filesQuery = _context.Files
                .Where(f => f.UserID == userId && f.IsDeleted == deletedOnly);

            if (parentFolderId.HasValue)
            {
                filesQuery = filesQuery.Where(f => f.FolderID == parentFolderId.Value);
            }
            else
            {
                filesQuery = filesQuery.Where(f => f.FolderID == null);
            }

            var files = await filesQuery
                .Select(f => new
                {
                    FileID = f.FileID,
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,
                    f.FolderID,
                    f.UploadedAt,
                    Type = "file"
                })
                .ToListAsync();

            return Ok(new { Folders = folders, Files = files });
        }
        /// <summary>
        /// Recursively gets all descendant folder IDs for a given parent folder
        /// </summary>
        private List<int> GetDescendantFolderIds(List<Drop1.Models.Folder> allFolders, int parentFolderId)
        {
            var result = new List<int>();
            var childFolders = allFolders.Where(f => f.ParentFolderID == parentFolderId).ToList();

            foreach (var childFolder in childFolders)
            {
                result.Add(childFolder.FolderID);
                // Recursively get descendants of this child folder
                result.AddRange(GetDescendantFolderIds(allFolders, childFolder.FolderID));
            }

            return result;
        }
    }
}
