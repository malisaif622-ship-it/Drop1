using Drop1.Api.Data;
using Drop1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Drop1.Api.Models;

namespace Drop1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FolderController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly string _basePath = @"C:\PROJECTS\Drop1(proj data folder)\";  // Storage root

        public FolderController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // CREATE FOLDER API
        // =========================
        [HttpPost("create")]
        public async Task<IActionResult> CreateFolder([FromQuery] string folderName, [FromQuery] int? parentFolderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            if (string.IsNullOrWhiteSpace(folderName)) return BadRequest("Folder name cannot be empty.");
            folderName = folderName.Trim();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return NotFound("User not found.");

            var userRoot = Path.Combine(_basePath, userId.ToString());
            Directory.CreateDirectory(userRoot);

            string parentPath = userRoot;
            if (parentFolderId.HasValue)
            {
                var parentFolder = await _context.Folders.FirstOrDefaultAsync(f =>
                    f.FolderID == parentFolderId.Value && f.UserID == userId && !f.IsDeleted);
                if (parentFolder == null) return NotFound("Parent folder not found.");
                parentPath = string.IsNullOrWhiteSpace(parentFolder.FolderPath) ? userRoot : parentFolder.FolderPath;
            }

            var siblings = await _context.Folders
                .Where(f => f.UserID == userId && f.ParentFolderID == parentFolderId && !f.IsDeleted)
                .Select(f => f.FolderName)
                .ToListAsync();

            string candidate = folderName;
            int counter = 2;
            string newFolderPath = Path.Combine(parentPath, candidate);

            while (siblings.Any(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase)) || Directory.Exists(newFolderPath))
            {
                candidate = $"{folderName} ({counter})";
                newFolderPath = Path.Combine(parentPath, candidate);
                counter++;
            }

            if (newFolderPath.Length > 500) return BadRequest("Folder path exceeds maximum length of 500 characters.");
            Directory.CreateDirectory(newFolderPath);

            var folder = new Folder
            {
                UserID = userId,
                FolderName = candidate,
                FolderPath = newFolderPath,
                ParentFolderID = parentFolderId,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Folders.Add(folder);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Folder created successfully",
                folderId = folder.FolderID,
                folderName = folder.FolderName,
                folderPath = folder.FolderPath,
                parentFolderId = folder.ParentFolderID,
                createdAt = folder.CreatedAt
            });
        }

        // =========================
        // UPLOAD FOLDER API
        // =========================
        [HttpPost("upload-folder")]
        public async Task<IActionResult> UploadFolder([FromForm] List<IFormFile> files, int? parentFolderId = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            var uploadedFiles = (files != null && files.Count > 0) ? files : Request.Form.Files.ToList();
            if (uploadedFiles == null || uploadedFiles.Count == 0) return BadRequest("No folder uploaded.");

            if (uploadedFiles.Count == 1)
            {
                var onlyName = uploadedFiles[0].FileName ?? "";
                if (!onlyName.Contains("/") && !onlyName.Contains("\\")) return BadRequest("Only folders can be uploaded. Please select a folder (not a single file).");
            }

            var userRootPath = Path.Combine(_basePath, userId.ToString());
            if (!Directory.Exists(userRootPath)) Directory.CreateDirectory(userRootPath);

            string parentPath = userRootPath;
            if (parentFolderId != null)
            {
                var parentFolder = await _context.Folders.FirstOrDefaultAsync(f => f.FolderID == parentFolderId && f.UserID == userId);
                if (parentFolder == null) return BadRequest("Parent folder not found.");
                parentPath = parentFolder.FolderPath;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return BadRequest("User not found.");

            decimal totalStorageMb = user.TotalStorageMB;
            decimal usedMb = user.UsedStorageMB;
            decimal uploadMb = uploadedFiles.Sum(f => Math.Round((decimal)f.Length / (1024 * 1024), 4));
            if (usedMb + uploadMb > totalStorageMb) return BadRequest($"Uploading exceeds your {totalStorageMb} MB storage limit.");

            bool anyHasPath = uploadedFiles.Any(f => (f.FileName ?? "").Contains("/") || (f.FileName ?? "").Contains("\\"));

            int? createdRootFolderId = null;
            string createdRootFolderPath = null!;
            if (!anyHasPath && uploadedFiles.Count > 1)
            {
                var baseName = "Uploaded Folder";
                var rootFolderName = baseName;
                int counter = 1;
                while (await _context.Folders.AnyAsync(f => f.FolderName == rootFolderName && f.ParentFolderID == parentFolderId && f.UserID == userId))
                {
                    counter++;
                    rootFolderName = $"{baseName} ({counter})";
                }

                createdRootFolderPath = Path.Combine(parentPath, rootFolderName);
                var newRoot = new Folder
                {
                    FolderName = rootFolderName,
                    ParentFolderID = parentFolderId,
                    UserID = userId,
                    FolderPath = createdRootFolderPath
                };
                _context.Folders.Add(newRoot);
                await _context.SaveChangesAsync();
                createdRootFolderId = newRoot.FolderID;

                if (!Directory.Exists(createdRootFolderPath)) Directory.CreateDirectory(createdRootFolderPath);
            }

            decimal totalAddedMb = 0m;

            foreach (var file in uploadedFiles)
            {
                if (file.Length <= 0) continue;

                var raw = file.FileName ?? "";
                var relative = raw.Replace("/", Path.DirectorySeparatorChar.ToString())
                                  .Replace("\\", Path.DirectorySeparatorChar.ToString());
                var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

                string currentPath = parentPath;
                int? currentParentId = parentFolderId;
                if (!anyHasPath && uploadedFiles.Count > 1)
                {
                    currentParentId = createdRootFolderId;
                    currentPath = createdRootFolderPath;
                }

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var folderName = parts[i];
                    currentPath = Path.Combine(currentPath, folderName);

                    var folderEntity = await _context.Folders
                        .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderID == currentParentId && f.UserID == userId);

                    if (folderEntity == null)
                    {
                        folderEntity = new Folder
                        {
                            FolderName = folderName,
                            ParentFolderID = currentParentId,
                            UserID = userId,
                            FolderPath = currentPath
                        };
                        _context.Folders.Add(folderEntity);
                        await _context.SaveChangesAsync();
                    }

                    currentParentId = folderEntity.FolderID;
                    if (!Directory.Exists(currentPath)) Directory.CreateDirectory(currentPath);
                }

                var fileName = parts.Last();
                var destPath = Path.Combine(currentPath, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using (var stream = new FileStream(destPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                decimal fileSizeMb = Math.Round((decimal)file.Length / (1024 * 1024), 4);
                totalAddedMb += fileSizeMb;

                string fileType = Path.GetExtension(fileName);
                fileType = string.IsNullOrWhiteSpace(fileType) ? (file.ContentType ?? "") : fileType.TrimStart('.').ToLowerInvariant();

                var dbFile = new FileItem
                {
                    FileName = fileName,
                    FileSizeMB = fileSizeMb,
                    FolderID = currentParentId.Value,
                    UserID = userId,
                    FilePath = destPath,
                    FileType = fileType,
                    UploadedAt = DateTime.UtcNow
                };

                _context.Files.Add(dbFile);
                await _context.SaveChangesAsync();
            }

            user.UsedStorageMB += totalAddedMb;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Ok("Folder uploaded successfully with hierarchy.");
        }

        // =========================
        // DOWNLOAD FOLDER API
        // =========================
        [HttpGet("download/{folderId}")]
        public async Task<IActionResult> DownloadFolder(int folderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            var folder = await _context.Folders.FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);
            if (folder == null) return NotFound("Folder not found.");
            if (!Directory.Exists(folder.FolderPath)) return NotFound("Folder path does not exist on disk.");

            var zipFileName = $"{folder.FolderName}.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), zipFileName);

            if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);

            ZipFile.CreateFromDirectory(folder.FolderPath, zipPath);

            var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
            return File(zipBytes, "application/zip", zipFileName);
        }

        // =========================
        // GET FOLDER DETAILS API (Google Drive style)
        // =========================
        [HttpGet("details/{folderId}")]
        public async Task<IActionResult> GetFolderDetails(int folderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);

            if (folder == null) return NotFound("Folder not found.");

            // ✅ Subfolders
            var subfolders = await _context.Folders
                .Where(f => f.ParentFolderID == folderId && f.UserID == userId && !f.IsDeleted)
                .Select(f => new
                {
                    f.FolderName,
                    f.CreatedAt
                })
                .ToListAsync();

            // ✅ Files
            var files = await _context.Files
                .Where(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted)
                .Select(f => new
                {
                    f.FileName,
                    f.FileSizeMB,
                    f.FileType,
                    f.UploadedAt
                })
                .ToListAsync();

            // ✅ Calculate folder size
            var totalSizeMB = files.Sum(f => f.FileSizeMB);

            return Ok(new
            {
                FolderName = folder.FolderName,
                CreatedAt = folder.CreatedAt,
                TotalFiles = files.Count,
                TotalSizeMB = Math.Round(totalSizeMB, 2),
                Subfolders = subfolders,
                Files = files
            });
        }

    }
}
