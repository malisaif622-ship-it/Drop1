using Drop1.Api.Data;
using Drop1.Api.Models;
using Drop1.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;

namespace Drop1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FolderController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly string _basePath = @"C:\Drop1\";  // Storage root
        private readonly IWebHostEnvironment _hostingEnvironment;

        public FolderController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateFolder([FromQuery] string folderName, [FromQuery] int? parentFolderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);
            // 1) Validate input
            if (string.IsNullOrWhiteSpace(folderName))
                return BadRequest("Folder name cannot be empty.");

            folderName = folderName.Trim();

            // 2) Ensure user exists
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return NotFound("User not found.");

            // 3) Ensure user root exists (lazy creation)
            var userRoot = Path.Combine(_basePath, userId.ToString());
            try
            {
                Directory.CreateDirectory(userRoot); // no-op if exists
            }
            catch (Exception)
            {
                return StatusCode(500, "Server error creating user storage directory.");
            }

            // 4) Resolve parent path (use userRoot when parent == null)
            string parentPath = userRoot;
            if (parentFolderId.HasValue)
            {
                var parentFolder = await _context.Folders.FirstOrDefaultAsync(f =>
                    f.FolderID == parentFolderId.Value && f.UserID == userId && !f.IsDeleted);

                if (parentFolder == null)
                    return NotFound("Parent folder not found.");

                parentPath = string.IsNullOrWhiteSpace(parentFolder.FolderPath) ? userRoot : parentFolder.FolderPath;
            }

            // Security check: ensure parentPath is inside user's root to avoid path escapes
            try
            {
                var normalizedParent = Path.GetFullPath(parentPath);
                var normalizedUserRoot = Path.GetFullPath(userRoot);
                if (!normalizedParent.StartsWith(normalizedUserRoot, StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Invalid parent folder location.");
            }
            catch (Exception)
            {
                return BadRequest("Invalid parent folder path.");
            }

            // 5) Compute unique name among siblings (DB + disk)
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

            // 6) Enforce 500 char max for FolderPath (your model constraint)
            if (newFolderPath.Length > 500)
                return BadRequest("Folder path exceeds maximum length of 500 characters.");

            // 7) Create directory on disk
            try
            {
                Directory.CreateDirectory(newFolderPath);
            }
            catch (Exception)
            {
                return StatusCode(500, "Failed to create folder on disk.");
            }

            // 8) Insert into DB (use your schema fields)
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

            // 9) Return result
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







        [HttpPost("upload-folder")]
        public async Task<IActionResult> UploadFolder(List<IFormFile> files, int? parentFolderId = null)
        {
            // ---------- 1) Auth ----------
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // ---------- 2) Accept files ----------
            var uploadedFiles = (files != null && files.Count > 0) ? files : Request.Form.Files.ToList();
            if (uploadedFiles == null || uploadedFiles.Count == 0)
                return BadRequest("No folder uploaded.");

            if (uploadedFiles.Count == 1)
            {
                var onlyName = uploadedFiles[0].FileName ?? "";
                if (!onlyName.Contains("/") && !onlyName.Contains("\\"))
                    return BadRequest("Only folders can be uploaded. Please select a folder (not a single file).");
            }

            // ---------- 3) Ensure root path ----------
            var userRootPath = Path.Combine("C:\\Drop1", userId.ToString());
            if (!Directory.Exists(userRootPath))
                Directory.CreateDirectory(userRootPath);

            string parentPath = userRootPath;
            if (parentFolderId != null)
            {
                var parentFolder = await _context.Folders
                    .FirstOrDefaultAsync(f => f.FolderID == parentFolderId && f.UserID == userId);
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

                if (!Directory.Exists(createdRootFolderPath))
                    Directory.CreateDirectory(createdRootFolderPath);
            }

            decimal totalAddedMb = 0m;

            // ---------- 6) Process files ----------
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

                    if (!Directory.Exists(currentPath))
                        Directory.CreateDirectory(currentPath);
                }

                var fileName = parts.Last();
                var destPath = Path.Combine(currentPath, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using (var stream = new FileStream(destPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                if (currentParentId == null)
                {
                    if (parentFolderId != null)
                        currentParentId = parentFolderId;
                    else
                    {
                        var userRootFolderName = Path.GetFileName(userRootPath) ?? userId.ToString();
                        var userRootFolder = await _context.Folders
                            .FirstOrDefaultAsync(f => f.FolderName == userRootFolderName && f.ParentFolderID == null && f.UserID == userId);

                        if (userRootFolder == null)
                        {
                            userRootFolder = new Folder
                            {
                                FolderName = userRootFolderName,
                                ParentFolderID = null,
                                UserID = userId,
                                FolderPath = userRootPath
                            };
                            _context.Folders.Add(userRootFolder);
                            await _context.SaveChangesAsync();
                        }
                        currentParentId = userRootFolder.FolderID;
                    }
                }

                decimal fileSizeMb = Math.Round((decimal)file.Length / (1024 * 1024), 4);
                totalAddedMb += fileSizeMb;

                string fileType = Path.GetExtension(fileName);
                fileType = string.IsNullOrWhiteSpace(fileType)
                    ? (file.ContentType ?? "")
                    : fileType.TrimStart('.').ToLowerInvariant();

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

            // ---------- 7) Update UsedStorageMB ----------
            user.UsedStorageMB += totalAddedMb;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Ok("Folder uploaded successfully with hierarchy.");
        }







        [HttpPut("rename")]
        public async Task<IActionResult> RenameFolder(int folderId, string newName)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (userId == null)
                return Unauthorized("User not logged in.");

            // ✅ 1) Get folder from DB
            var folder = await _context.Folders.FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID.ToString() == userId);
            if (folder == null)
                return NotFound("Folder not found.");

            // ✅ 2) Save old paths before changes
            string oldFolderPath = folder.FolderPath;
            string oldFolderName = folder.FolderName;

            // ✅ 3) Parent directory
            string? parentPath = Path.GetDirectoryName(oldFolderPath);

            // ✅ 4) Ensure unique name (like Windows Explorer renaming)
            string finalName = newName;
            int counter = 2;
            while (await _context.Folders.AnyAsync(f =>
                f.UserID.ToString() == userId &&
                f.FolderID != folderId &&
                f.FolderPath == Path.Combine(parentPath ?? "", finalName)))
            {
                finalName = $"{newName} ({counter++})";
            }

            // ✅ 5) Compute new folder path
            string newFolderPath = Path.Combine(parentPath ?? "", finalName);

            // ✅ 6) Physical paths
            var rootPath = Path.Combine(_hostingEnvironment.ContentRootPath, "UserData", userId);
            var oldPhysicalPath = Path.Combine(rootPath, oldFolderPath);
            var newPhysicalPath = Path.Combine(rootPath, newFolderPath);

            // ✅ 7) Rename physically on disk
            if (Directory.Exists(oldPhysicalPath))
            {
                Directory.Move(oldPhysicalPath, newPhysicalPath);
            }

            // ✅ 8) Update DB
            folder.FolderName = finalName;
            folder.FolderPath = newFolderPath;

            // Update child folders
            var childFolders = await _context.Folders
                .Where(f => f.UserID.ToString() == userId && f.FolderPath.StartsWith(oldFolderPath))
                .ToListAsync();

            foreach (var child in childFolders)
            {
                child.FolderPath = child.FolderPath.Replace(oldFolderPath, newFolderPath);
            }

            // Update child files
            var childFiles = await _context.Files
                .Where(f => f.UserID.ToString() == userId && f.FilePath.StartsWith(oldFolderPath))
                .ToListAsync();

            foreach (var file in childFiles)
            {
                file.FilePath = file.FilePath.Replace(oldFolderPath, newFolderPath);
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Folder renamed successfully", newName = finalName });
        }







        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFolder(int folderId)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (userId == null)
                return Unauthorized("User not logged in.");

            // 1) Find the folder in DB
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID.ToString() == userId);
            if (folder == null)
                return NotFound("Folder not found.");

            // 2) User root folder
            var userRootPath = Path.Combine(_hostingEnvironment.ContentRootPath, "UserData", userId);

            // 3) RecycleBin path inside the user folder
            var recycleBinPath = Path.Combine(userRootPath, "RecycleBin");

            // ✅ Create RecycleBin if it doesn’t exist
            if (!Directory.Exists(recycleBinPath))
                Directory.CreateDirectory(recycleBinPath);

            // 4) Physical path of the folder being deleted
            string folderPhysicalPath = Path.Combine(userRootPath, folder.FolderPath);
            if (!Directory.Exists(folderPhysicalPath))
                return NotFound("Physical folder not found.");

            // 5) Unique folder name inside RecycleBin
            string recycleFolderName = folder.FolderName;
            string recycleDestinationPath = Path.Combine(recycleBinPath, recycleFolderName);
            int counter = 2;

            while (Directory.Exists(recycleDestinationPath))
            {
                recycleFolderName = $"{folder.FolderName} ({counter})";
                recycleDestinationPath = Path.Combine(recycleBinPath, recycleFolderName);
                counter++;
            }

            // 6) Copy folder to RecycleBin, then delete source
            CopyDirectory(folderPhysicalPath, recycleDestinationPath);
            Directory.Delete(folderPhysicalPath, true);

            // 7) Update DB → mark folder & files as deleted
            var foldersToDelete = await _context.Folders
                .Where(f => f.UserID.ToString() == userId && f.FolderPath.StartsWith(folder.FolderPath))
                .ToListAsync();

            foreach (var f in foldersToDelete)
                f.IsDeleted = true;

            var filesToDelete = await _context.Files
                .Where(f => f.UserID.ToString() == userId && f.FilePath.StartsWith(folder.FolderPath))
                .ToListAsync();

            foreach (var file in filesToDelete)
                file.IsDeleted = true;

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Folder moved to RecycleBin as {recycleFolderName}" });
        }

        // ✅ Recursive safe copy method
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Copy(file, targetFilePath, true);
            }

            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string targetDirPath = Path.Combine(destDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirPath);
            }
        }
    }
}
