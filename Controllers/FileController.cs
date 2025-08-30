using Drop1.Api.Data;
using Drop1.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Drop1.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly string _basePath = @"C:\Drop1\";  // Storage root
        private readonly IWebHostEnvironment _hostingEnvironment;

        public FileController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        // =========================
        // CREATE FILE API
        // =========================
        [HttpPost("create-file")]
        public async Task<IActionResult> CreateFile([FromQuery] string fileName, [FromQuery] int? parentFolderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // 1) Validate input
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("File name cannot be empty.");

            fileName = fileName.Trim();

            // 2) Ensure user exists
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return NotFound("User not found.");

            // 3) Ensure user root exists
            var userRoot = Path.Combine(_basePath, userId.ToString());
            try
            {
                Directory.CreateDirectory(userRoot);
            }
            catch
            {
                return StatusCode(500, "Server error creating user storage directory.");
            }

            // 4) Resolve parent folder path
            string parentPath = userRoot;
            if (parentFolderId.HasValue)
            {
                var parentFolder = await _context.Folders.FirstOrDefaultAsync(f =>
                    f.FolderID == parentFolderId.Value && f.UserID == userId && !f.IsDeleted);

                if (parentFolder == null)
                    return NotFound("Parent folder not found.");

                parentPath = string.IsNullOrWhiteSpace(parentFolder.FolderPath) ? userRoot : parentFolder.FolderPath;
            }

            // Security check: ensure parentPath stays inside userRoot
            try
            {
                var normalizedParent = Path.GetFullPath(parentPath);
                var normalizedUserRoot = Path.GetFullPath(userRoot);
                if (!normalizedParent.StartsWith(normalizedUserRoot, StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Invalid parent folder location.");
            }
            catch
            {
                return BadRequest("Invalid parent folder path.");
            }

            // 5) Extract extension
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                ext = ".txt"; // default extension

            // 6) Ensure uniqueness among siblings
            var siblingFiles = await _context.Files
                .Where(f => f.UserID == userId && f.FolderID == parentFolderId && !f.IsDeleted)
                .Select(f => f.FileName + f.FileType)
                .ToListAsync();

            string candidateName = nameWithoutExt;
            string candidateExt = ext;
            string newFilePath = Path.Combine(parentPath, candidateName + candidateExt);

            int counter = 2;
            while (siblingFiles.Any(s => string.Equals(s, candidateName + candidateExt, StringComparison.OrdinalIgnoreCase))
                   || System.IO.File.Exists(newFilePath))
            {
                candidateName = $"{nameWithoutExt} ({counter})";
                newFilePath = Path.Combine(parentPath, candidateName + candidateExt);
                counter++;
            }

            if (newFilePath.Length > 500)
                return BadRequest("File path exceeds maximum length of 500 characters.");

            // 7) Create physical file
            try
            {
                using (var fs = System.IO.File.Create(newFilePath)) { }
            }
            catch
            {
                return StatusCode(500, "Failed to create file on disk.");
            }

            // 8) Insert into DB
            var file = new Api.Models.FileItem
            {
                UserID = userId,
                FileName = candidateName,
                FileType = candidateExt,
                FilePath = newFilePath,
                FolderID = parentFolderId,
                UploadedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Files.Add(file);
            await _context.SaveChangesAsync();

            // 9) Return result
            return Ok(new
            {
                message = "File created successfully",
                fileId = file.FileID,
                fileName = file.FileName,
                fileType = file.FileType,
                filePath = file.FilePath,
                folderId = file.FolderID,
                createdAt = file.UploadedAt
            });
        }

        // =========================
        // UPLOAD FILE API
        // =========================
        [HttpPost("upload-file")]
        public async Task<IActionResult> UploadFile(List<IFormFile> files, int? parentFolderId = null)
        {
            // ---------- 1) Auth ----------
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // ---------- 2) Accept files ----------
            var uploadedFiles = (files != null && files.Count > 0)
                                ? files
                                : Request.Form.Files.ToList();
            if (uploadedFiles == null || uploadedFiles.Count == 0)
                return BadRequest("No file uploaded.");

            // ---------- 3) Ensure root path ----------
            var userRootPath = Path.Combine("C:\\Drop1", userId.ToString());
            if (!Directory.Exists(userRootPath))
                Directory.CreateDirectory(userRootPath);

            string parentPath = userRootPath;
            if (parentFolderId != null)
            {
                var parentFolder = await _context.Folders
                    .FirstOrDefaultAsync(f => f.FolderID == parentFolderId &&
                                              f.UserID == userId &&
                                              !f.IsDeleted);
                if (parentFolder == null)
                    return BadRequest("Parent folder not found.");

                parentPath = parentFolder.FolderPath;
            }

            // ---------- 4) Get user storage info ----------
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
                return BadRequest("User not found.");

            decimal totalStorageMb = user.TotalStorageMB;
            decimal usedMb = user.UsedStorageMB;

            // ---------- 5) Quota check ----------
            decimal uploadMb = uploadedFiles.Sum(f => Math.Round((decimal)f.Length / (1024 * 1024), 4));
            if (usedMb + uploadMb > totalStorageMb)
                return BadRequest($"Uploading exceeds your {totalStorageMb} MB storage limit.");

            decimal totalAddedMb = 0m;

            // ---------- 6) Process each file ----------
            foreach (var file in uploadedFiles)
            {
                if (file.Length <= 0) continue;

                var originalFileName = file.FileName.Trim();
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
                var ext = Path.GetExtension(originalFileName);
                if (string.IsNullOrWhiteSpace(ext))
                    ext = ".txt"; // default extension

                // check existing names in DB
                var siblings = await _context.Files
                    .Where(f => f.UserID == userId &&
                                f.FolderID == parentFolderId &&
                                !f.IsDeleted)
                    .Select(f => f.FileName + "." + f.FileType)
                    .ToListAsync();

                string candidateName = fileNameWithoutExt;
                string candidateExt = ext;
                string destPath = Path.Combine(parentPath, candidateName + candidateExt);
                int counter = 2;

                // ensure unique filename in this folder
                while (siblings.Any(s => string.Equals(s, candidateName + candidateExt, StringComparison.OrdinalIgnoreCase))
                       || System.IO.File.Exists(destPath))
                {
                    candidateName = $"{fileNameWithoutExt} ({counter})";
                    destPath = Path.Combine(parentPath, candidateName + candidateExt);
                    counter++;
                }

                // create file physically
                using (var stream = new FileStream(destPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                decimal fileSizeMb = Math.Round((decimal)file.Length / (1024 * 1024), 4);
                totalAddedMb += fileSizeMb;

                // insert into DB
                var dbFile = new FileItem
                {
                    FileName = candidateName,
                    FileType = candidateExt.TrimStart('.').ToLowerInvariant(),
                    FileSizeMB = fileSizeMb,
                    FolderID = parentFolderId,
                    UserID = userId,
                    FilePath = destPath,
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.Files.Add(dbFile);
                await _context.SaveChangesAsync();
            }

            // ---------- 7) Update UsedStorageMB ----------
            user.UsedStorageMB += totalAddedMb;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Ok("File(s) uploaded successfully.");
        }

        // =========================
        // RENAME FILE API
        // =========================
        [HttpPut("rename-file")]
        public async Task<IActionResult> RenameFile(int fileId, string newName)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (userId == null)
                return Unauthorized("User not logged in.");

            // ✅ 1) Get file from DB
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID.ToString() == userId && !f.IsDeleted);
            if (file == null)
                return NotFound("File not found.");

            // ✅ 2) Extract extension (type) & base name
            string extension = Path.GetExtension(file.FileName); // e.g. ".txt"
            string baseName = Path.GetFileNameWithoutExtension(newName);

            // keep the filetype from DB (not just extension string)
            string? fileType = file.FileType;

            string finalName = $"{baseName}{extension}";

            // ✅ 3) Ensure uniqueness only if same name + same type exists
            int counter = 2;
            while (await _context.Files.AnyAsync(f =>
                f.UserID.ToString() == userId &&
                f.FileID != fileId &&
                f.FolderID == file.FolderID && // same folder
                f.FileType == fileType && // same type
                f.FileName == finalName)) // same name
            {
                finalName = $"{baseName} ({counter++}){extension}";
            }

            // ✅ 4) Compute new file path
            string? parentPath = Path.GetDirectoryName(file.FilePath);
            string newFilePath = Path.Combine(parentPath ?? "", finalName);

            // ✅ 5) Physical paths
            var rootPath = Path.Combine(_hostingEnvironment.ContentRootPath, "UserData", userId);
            var oldPhysicalPath = Path.Combine(rootPath, file.FilePath);
            var newPhysicalPath = Path.Combine(rootPath, newFilePath);

            // ✅ 6) Rename physically
            if (System.IO.File.Exists(oldPhysicalPath))
            {
                System.IO.File.Move(oldPhysicalPath, newPhysicalPath);
            }

            // ✅ 7) Update DB
            file.FileName = finalName;
            file.FilePath = newFilePath;

            await _context.SaveChangesAsync();

            return Ok(new { message = "File renamed successfully", newName = finalName });
        }

        // =========================
        // DELETE FILE API
        // =========================
        [HttpDelete("delete-file")]
        public async Task<IActionResult> DeleteFile(int fileId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // ✅ 1) Get the file to delete
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);

            if (file == null)
                return NotFound("File not found.");

            string filePath = file.FilePath;

            // ✅ 2) User root
            var userRoot = Path.Combine(_basePath, userId.ToString());

            // ✅ 3) RecycleBin inside user's root
            var recycleBinPath = Path.Combine(userRoot, "RecycleBin");
            try
            {
                if (!Directory.Exists(recycleBinPath))
                    Directory.CreateDirectory(recycleBinPath);
            }
            catch
            {
                return StatusCode(500, "Error creating recycle bin directory.");
            }

            string fileName = Path.GetFileName(filePath);
            string destinationPath = Path.Combine(recycleBinPath, fileName);
            try
            {
                System.IO.File.Move(filePath, destinationPath);
            }
            catch
            {
                return StatusCode(500, "Error moving file to recycle bin.");
            }

            // ✅ 6) Mark only this file as deleted
            file.IsDeleted = true;

            await _context.SaveChangesAsync();

            return Ok(new { message = "File moved to recycle bin successfully." });
        }

        // =========================
        // RECOVER FILE API
        // =========================
        [HttpPut("recover-file/{fileId}")]
        public async Task<IActionResult> RecoverFile(int fileId)
        {
            // 0) Auth
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // 1) Load target file (must be deleted)
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && f.IsDeleted);

            if (file == null)
                return NotFound("Deleted file not found.");

            string originalFilePath = file.FilePath;
            string fileName = Path.GetFileNameWithoutExtension(originalFilePath);
            string extension = Path.GetExtension(originalFilePath);
            string originalFolder = Path.GetDirectoryName(originalFilePath)!;

            // 2) Build user root + RecycleBin path
            var userRoot = Path.Combine(_basePath, userId.ToString());
            var recycleBinPath = Path.Combine(userRoot, "RecycleBin");

            if (!Directory.Exists(recycleBinPath))
                return NotFound("Recycle Bin not found for this user.");

            // 3) Locate file in RecycleBin
            string? recycleCurrentPath = null;
            var exactCandidate = Path.Combine(recycleBinPath, file.FileName + file.FileType);
            if (System.IO.File.Exists(exactCandidate))
            {
                recycleCurrentPath = exactCandidate;
            }
            else
            {
                var candidates = Directory.GetFiles(recycleBinPath, file.FileName + "*" + file.FileType, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                    .ToList();

                recycleCurrentPath = candidates.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(recycleCurrentPath) || !System.IO.File.Exists(recycleCurrentPath))
                return NotFound("File not found inside Recycle Bin.");

            // 4) Ensure original folder exists
            if (!Directory.Exists(originalFolder))
                Directory.CreateDirectory(originalFolder);

            // 5) Resolve name conflict (same FileName + FileType in same folder)
            string targetFilePath = originalFilePath;
            string baseName = fileName;
            int counter = 2;

            while (System.IO.File.Exists(targetFilePath) ||
                   await _context.Files.AnyAsync(f =>
                       f.UserID == userId &&
                       !f.IsDeleted &&
                       f.FolderID == file.FolderID &&
                       f.FileName == Path.GetFileNameWithoutExtension(targetFilePath) &&
                       f.FileType == extension))
            {
                string newName = $"{baseName} ({counter})";
                targetFilePath = Path.Combine(originalFolder, newName + extension);
                counter++;
            }

            string finalFileName = Path.GetFileNameWithoutExtension(targetFilePath);

            // 6) Move file from RecycleBin back
            try
            {
                System.IO.File.Move(recycleCurrentPath, targetFilePath);
            }
            catch
            {
                return StatusCode(500, "Failed to move file back from Recycle Bin.");
            }

            // 7) Update DB: restore and adjust path
            file.IsDeleted = false;
            file.FilePath = targetFilePath;
            file.FileName = finalFileName;
            file.FileType = extension;

            await _context.SaveChangesAsync();

            return Ok(new { message = $"File restored successfully as '{finalFileName}{extension}'." });
        }

        // =========================
        // DOWNLOAD FILE API
        // =========================
        [HttpGet("download-file/{fileId}")]
        public async Task<IActionResult> DownloadFile(int fileId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            // ✅ Check if file exists in DB and belongs to the user
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);

            if (file == null)
                return NotFound("File not found.");

            if (!System.IO.File.Exists(file.FilePath))
                return NotFound("File path does not exist on disk.");

            var fileName = Path.GetFileName(file.FilePath);
            var mimeType = "application/octet-stream"; // you can also detect actual mime type if needed

            // ✅ Stream file to client
            return PhysicalFile(file.FilePath, mimeType, fileName);
        }

        // =========================
        // GET FILE DETAILS API
        // =========================
        [HttpGet("file/details/{fileId}")]
        public async Task<IActionResult> GetFileDetails(int fileId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);

            if (file == null) return NotFound("File not found.");

            return Ok(new
            {
                FileName = file.FileName,
                FileType = file.FileType,
                FileSizeMB = Math.Round(file.FileSizeMB, 2),
                UploadedAt = file.UploadedAt
            });
        }
    }
}