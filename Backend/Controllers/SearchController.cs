using Drop1.Api.Data;
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
        public async Task<IActionResult> Search([FromQuery] string keyword)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Keyword cannot be empty.");

            var rootPath = _config["StorageSettings:RootPath"];

            var dbFolders = await _context.Folders
                .Where(f => f.UserID == userId && !f.IsDeleted && f.FolderName.Contains(keyword))
                .ToListAsync();

            var folders = dbFolders
                .Where(f => System.IO.Directory.Exists(Path.Combine(rootPath, f.FolderPath)))
                .Select(f => new
                {
                    f.FolderID,
                    f.FolderName,
                    f.ParentFolderID,
                    f.CreatedAt
                })
                .ToList();

            var dbFiles = await _context.Files
                .Where(f => f.UserID == userId && !f.IsDeleted && f.FileName.Contains(keyword))
                .ToListAsync();

            var files = dbFiles
                .Where(f => System.IO.File.Exists(Path.Combine(rootPath, f.FilePath)))
                .Select(f => new
                {
                    f.FileID,
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
                    f.FolderID,
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
                    f.FileID,
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
                    f.FolderID,
                    f.FolderName,
                    f.CreatedAt
                })
                .ToListAsync();

            var files = await _context.Files
                .Where(file => file.UserID == userId && file.FolderID == parentFolderId && !file.IsDeleted)
                .Select(file => new
                {
                    file.FileID,
                    file.FileName,
                    file.FileSizeMB,
                    file.FileType,
                    file.UploadedAt
                })
                .ToListAsync();

            return Ok(new { Folders = folders, Files = files });
        }
    }
}
