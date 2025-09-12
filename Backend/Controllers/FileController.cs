using Drop1.Api.Data;
using Drop1.Api.Models;
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

        // Pakistan Standard Time (UTC+5)
        private static readonly TimeZoneInfo PakistanTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Pakistan Standard Time", TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");

        private DateTime GetPakistanTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PakistanTimeZone);
        }

        public FileController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        // =========================
        // UPLOAD FILE API
        // =========================
        [HttpPost("upload-file")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
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
                    UploadedAt = GetPakistanTime(),
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
        [HttpPut("rename/{fileId}")]
        public async Task<IActionResult> RenameFile(int fileId, [FromQuery] string newName)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            if (string.IsNullOrWhiteSpace(newName))
                return BadRequest("File name cannot be empty.");

            // ✅ Load file & verify ownership
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);
            if (file == null)
                return NotFound("File not found.");

            // ✅ Normalize extension (from FileType)
            string normalizedFileType = (file.FileType ?? "").Trim().TrimStart('.');
            string extension = string.IsNullOrEmpty(normalizedFileType) ? "" : "." + normalizedFileType;

            // ✅ Ensure newName doesn’t include extension
            string baseName = Path.GetFileNameWithoutExtension(newName).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
                return BadRequest("Invalid new name.");

            string finalName = baseName; // DB should not include extension
            string finalFileNameWithExt = finalName + extension;

            // ✅ Ensure uniqueness (case-insensitive) in same folder
            string compareNameLower = finalName.ToLower();
            bool exists = await _context.Files.AnyAsync(f =>
                f.UserID == userId &&
                f.FileID != fileId &&
                f.FolderID == file.FolderID &&
                !f.IsDeleted &&
                f.FileName.ToLower() == compareNameLower &&
                f.FileType.ToLower() == normalizedFileType.ToLower());

            int counter = 2;
            while (exists)
            {
                finalName = $"{baseName} ({counter++})";
                finalFileNameWithExt = finalName + extension;

                compareNameLower = finalName.ToLower();
                exists = await _context.Files.AnyAsync(f =>
                    f.UserID == userId &&
                    f.FileID != fileId &&
                    f.FolderID == file.FolderID &&
                    !f.IsDeleted &&
                    f.FileName.ToLower() == compareNameLower &&
                    f.FileType.ToLower() == normalizedFileType.ToLower());
            }

            // ✅ Build new paths
            string parentPath = Path.GetDirectoryName(file.FilePath) ?? "";
            string newFilePath = Path.Combine(parentPath, finalFileNameWithExt);

            var rootPath = Path.Combine(_hostingEnvironment.ContentRootPath, "UserData", userIdStr);
            string oldPhysicalPath = Path.Combine(rootPath, file.FilePath);
            string newPhysicalPath = Path.Combine(rootPath, newFilePath);

            try
            {
                if (!System.IO.File.Exists(oldPhysicalPath))
                    return NotFound("Physical file not found on disk.");

                Directory.CreateDirectory(Path.GetDirectoryName(newPhysicalPath)!);
                System.IO.File.Move(oldPhysicalPath, newPhysicalPath);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error renaming file: {ex.Message}");
            }

            // ✅ Update DB (FileName = no extension, FilePath = with extension, FileType unchanged)
            file.FileName = finalName;
            file.FilePath = newFilePath;
            file.FileType = normalizedFileType;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "File renamed successfully",
                FileID = file.FileID,
                FileName = file.FileName,   // no extension
                FilePath = file.FilePath,   // includes extension
                FileType = file.FileType,   // extension only
                FolderID = file.FolderID,
                FileSizeMB = file.FileSizeMB,
                UploadedAt = file.UploadedAt
            });
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

            // 1) Get the file record
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);

            if (file == null)
                return NotFound("File not found.");

            string filePath = file.FilePath;

            // 2) Build user recycle bin path
            var userRoot = Path.Combine(_basePath, userId.ToString());
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

            // 3) Physically move file into recycle bin
            string fileName = Path.GetFileName(filePath);
            string destinationPath = Path.Combine(recycleBinPath, fileName);

            try
            {
                // Handle duplicate file names in recycle bin
                int counter = 1;
                while (System.IO.File.Exists(destinationPath))
                {
                    string baseName = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destinationPath = Path.Combine(recycleBinPath, $"{baseName}({counter++}){ext}");
                }

                System.IO.File.Move(filePath, destinationPath);
            }
            catch
            {
                return StatusCode(500, "Error moving file to recycle bin.");
            }

            // 4) Mark as deleted in DB (path unchanged)
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
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // 1) Find deleted file record
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && f.IsDeleted);

            if (file == null)
                return NotFound("Deleted file not found.");

            // From DB
            string baseName = file.FileName;  // without extension
            string extension = string.IsNullOrEmpty(file.FileType) ? "" : "." + file.FileType;

            // 2) RecycleBin path
            var userRoot = Path.Combine(_basePath, userId.ToString());
            var recycleBinPath = Path.Combine(userRoot, "RecycleBin");

            if (!Directory.Exists(recycleBinPath))
                return NotFound("Recycle Bin not found for this user.");

            // 3) Look inside recycle bin for a match
            string? recycleCurrentPath = Directory.GetFiles(recycleBinPath, $"{baseName}*{extension}", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (string.IsNullOrEmpty(recycleCurrentPath) || !System.IO.File.Exists(recycleCurrentPath))
                return NotFound($"File '{baseName}{extension}' not found inside Recycle Bin.");

            // 4) Original folder (from stored path in DB)
            string originalFolder = Path.GetDirectoryName(file.FilePath)!;
            if (!Directory.Exists(originalFolder))
                Directory.CreateDirectory(originalFolder);

            // 5) Resolve conflicts
            string targetFilePath = file.FilePath; // original location
            int counter = 2;
            while (System.IO.File.Exists(targetFilePath) ||
                   await _context.Files.AnyAsync(f =>
                       f.UserID == userId &&
                       !f.IsDeleted &&
                       f.FolderID == file.FolderID &&
                       f.FileName == Path.GetFileNameWithoutExtension(targetFilePath) &&
                       f.FileType == file.FileType))
            {
                string newName = $"{baseName} ({counter++})";
                targetFilePath = Path.Combine(originalFolder, newName + extension);
            }

            // 6) Move back physically
            try
            {
                System.IO.File.Move(recycleCurrentPath, targetFilePath);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to move file back from Recycle Bin. {ex.Message}");
            }

            // 7) Update DB (just clear IsDeleted)
            file.IsDeleted = false;

            await _context.SaveChangesAsync();

            return Ok(new { message = $"File restored successfully as '{Path.GetFileName(targetFilePath)}'." });
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
            // Expose Content-Disposition header for CORS so frontend can read filename
            Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
            return PhysicalFile(file.FilePath, mimeType, fileName);
        }

        // =========================
        // GET FILE DETAILS API
        // =========================
        //[HttpGet("details")]
        //public async Task<IActionResult> GetFileDetails([FromQuery] int fileId)
        //{
        //    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
        //    if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        //    int userId = int.Parse(userIdStr);

        //    var file = await _context.Files
        //        .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && !f.IsDeleted);

        //    if (file == null) return NotFound("File not found.");

        //    return Ok(new
        //    {
        //        FileID = file.FileID,
        //        FileName = file.FileName,
        //        FileType = file.FileType,
        //        FileSizeMB = file.FileSizeMB,
        //        UploadedAt = file.UploadedAt,
        //        FolderID = file.FolderID
        //    });
        //}

        // =========================
        // PERMANENT DELETE FILE API
        // =========================
        [HttpDelete("permanent-delete")]
        public async Task<IActionResult> PermanentDeleteFile([FromQuery] int fileId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId && f.IsDeleted);

            if (file == null)
                return NotFound("File not found in recycle bin.");

            try
            {
                // Capture size to decrease user storage
                decimal removedMb = file.FileSizeMB;

                // Correct user recycle bin path under configured base path
                var userRoot = Path.Combine(_basePath, userId.ToString());
                var recycleBinDir = Path.Combine(userRoot, "RecycleBin");
                var fileNameOnly = Path.GetFileName(file.FilePath);
                var recycleCandidate = Path.Combine(recycleBinDir, fileNameOnly);

                // Try deleting from recycle bin first
                if (System.IO.File.Exists(recycleCandidate))
                {
                    System.IO.File.Delete(recycleCandidate);
                }
                // If not found in recycle bin, try deleting from original location
                else if (System.IO.File.Exists(file.FilePath))
                {
                    System.IO.File.Delete(file.FilePath);
                }

                // Remove from database
                _context.Files.Remove(file);

                // Update user's used storage
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
                if (user != null)
                {
                    user.UsedStorageMB = Math.Max(0, user.UsedStorageMB - removedMb);
                    _context.Users.Update(user);
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "File permanently deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error permanently deleting file: {ex.Message}");
            }
        }


        // =========================
        // FILE DETAILS API
        // =========================
        [HttpGet("details")]
        public async Task<IActionResult> GetFileDetails([FromQuery] int fileId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileID == fileId && f.UserID == userId);

            if (file == null)
                return NotFound("File not found.");

            return Ok(new
            {
                FileID = file.FileID,
                FileName = file.FileName,
                FileType = file.FileType,
                FileSizeMB = file.FileSizeMB,
                FilePath = file.FilePath,
                FolderID = file.FolderID,
                UploadedAt = file.UploadedAt,
                IsDeleted = file.IsDeleted
            });
        }
    }
}