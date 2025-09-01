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
            // ✅ Get logged-in User
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();
            int userId = int.Parse(userIdStr);

            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Keyword cannot be empty.");

            // ✅ Root storage path from appsettings.json
            var rootPath = _config["StorageSettings:RootPath"];

            // ✅ Search Folders in DB
            var dbFolders = await _context.Folders
                .Where(f => f.UserID == userId && !f.IsDeleted && f.FolderName.Contains(keyword))
                .ToListAsync();

            var folders = dbFolders
                .Where(f => System.IO.Directory.Exists(Path.Combine(rootPath, f.FolderPath))) // ✅ check disk
                .Select(f => new
                {
                    f.FolderName,
                    f.CreatedAt
                })
                .ToList();

            // ✅ Search Files in DB
            var dbFiles = await _context.Files
                .Where(f => f.UserID == userId && !f.IsDeleted && f.FileName.Contains(keyword))
                .ToListAsync();

            var files = dbFiles
                .Where(f => System.IO.File.Exists(Path.Combine(rootPath, f.FilePath))) // ✅ check disk
                .Select(f => new
                {
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,s
                    f.UploadedAt
                })
                .ToList();

            return Ok(new
            {
                Keyword = keyword,
                Folders = folders,
                Files = files
            });
        }
    }
}
