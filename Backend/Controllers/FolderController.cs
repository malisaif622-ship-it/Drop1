using Drop1.Api.Data;
using Drop1.Api.Models;
using Drop1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
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

        // Pakistan Standard Time (UTC+5)
        private static readonly TimeZoneInfo PakistanTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Pakistan Standard Time", TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");

        private DateTime GetPakistanTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PakistanTimeZone);
        }

        public FolderController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        // =========================
        // CREATE FOLDER API
        // =========================
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
                CreatedAt = GetPakistanTime(),
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

        // =========================
        // UPLOAD FOLDER API
        // =========================
        [HttpPost("upload-folder")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
        public async Task<IActionResult> UploadFolder(List<IFormFile> files, int? parentFolderId = null, string? rootFolderName = null)
        {
            // ---------- 1) Auth ----------
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // ---------- 2) Accept files ----------
            var uploadedFiles = (files != null && files.Count > 0) ? files : Request.Form.Files.ToList();
            // Allow empty-folder creation when no files are uploaded
            bool isEmptyFolderRequest = uploadedFiles == null || uploadedFiles.Count == 0;

            // ---------- 3) Ensure root path ----------
            var userRootPath = Path.Combine("C:\\Drop1", userId.ToString());
            if (!Directory.Exists(userRootPath))
                Directory.CreateDirectory(userRootPath);

            string parentPath = userRootPath;
            if (parentFolderId != null)
            {
                var parentFolder = await _context.Folders
                    .FirstOrDefaultAsync(f => f.FolderID == parentFolderId && f.UserID == userId && !f.IsDeleted);
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

            // ---------- 6) Check for folder structure ----------
            bool anyHasPath = !isEmptyFolderRequest && uploadedFiles.Any(f =>
                (f.FileName ?? "").Contains("/") || (f.FileName ?? "").Contains("\\"));

            // ---------- 7) Handle single file without path ----------
            if (!anyHasPath && !isEmptyFolderRequest && uploadedFiles.Count == 1)
            {
                var file = uploadedFiles.First();
                if (file.Length <= 0) return BadRequest("Empty file.");

                var fileName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(parentPath, fileName);

                using (var stream = new FileStream(destPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                var fileSizeMb = Math.Round((decimal)file.Length / (1024 * 1024), 4);

                var dbFile = new FileItem
                {
                    FileName = Path.GetFileNameWithoutExtension(fileName),
                    FileSizeMB = fileSizeMb,
                    FolderID = parentFolderId ?? (await EnsureUserRootFolder(userId, userRootPath)),
                    UserID = userId,
                    FilePath = destPath,
                    FileType = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant(),
                    UploadedAt = GetPakistanTime()
                };

                _context.Files.Add(dbFile);
                await _context.SaveChangesAsync();

                user.UsedStorageMB += fileSizeMb;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok("Single file uploaded successfully.");
            }

            // ---------- 8) Otherwise, process as folder upload ----------
            int? createdRootFolderId = null;
            string createdRootFolderPath = null!;

            // Handle explicit empty-folder creation via rootFolderName
            if (isEmptyFolderRequest)
            {
                var emptyBaseName = string.IsNullOrWhiteSpace(rootFolderName) ? "New Folder" : rootFolderName!.Trim();
                var emptyUniqueName = emptyBaseName;
                int emptyCounter = 2;
                var emptyCandidatePath = Path.Combine(parentPath, emptyUniqueName);
                while (Directory.Exists(emptyCandidatePath) || await _context.Folders.AnyAsync(f => f.UserID == userId && f.ParentFolderID == parentFolderId && !f.IsDeleted && f.FolderName == emptyUniqueName))
                {
                    emptyUniqueName = $"{emptyBaseName} ({emptyCounter++})";
                    emptyCandidatePath = Path.Combine(parentPath, emptyUniqueName);
                }

                Directory.CreateDirectory(emptyCandidatePath);
                var emptyFolder = new Folder
                {
                    UserID = userId,
                    FolderName = emptyUniqueName,
                    FolderPath = emptyCandidatePath,
                    ParentFolderID = parentFolderId,
                    CreatedAt = GetPakistanTime(),
                    IsDeleted = false
                };
                _context.Folders.Add(emptyFolder);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Empty folder created successfully.", folderId = emptyFolder.FolderID, folderName = emptyFolder.FolderName });
            }

            if (!anyHasPath && uploadedFiles.Count > 1)
            {
                var baseRootName = "Uploaded Folder";
                var uniqueRootName = baseRootName;
                int nameCounter = 1;
                while (await _context.Folders.AnyAsync(f => f.FolderName == uniqueRootName && f.ParentFolderID == parentFolderId && f.UserID == userId))
                {
                    nameCounter++;
                    uniqueRootName = $"{baseRootName} ({nameCounter})";
                }

                createdRootFolderPath = Path.Combine(parentPath, uniqueRootName);
                var newRoot = new Folder
                {
                    FolderName = uniqueRootName,
                    ParentFolderID = parentFolderId,
                    UserID = userId,
                    FolderPath = createdRootFolderPath,
                    CreatedAt = GetPakistanTime()
                };
                _context.Folders.Add(newRoot);
                await _context.SaveChangesAsync();
                createdRootFolderId = newRoot.FolderID;

                if (!Directory.Exists(createdRootFolderPath))
                    Directory.CreateDirectory(createdRootFolderPath);
            }

            decimal totalAddedMb = 0m;

            // ---------- 9) Process files ----------
            // Special handling: if paths are present (true folder upload), ensure top-level folder(s) use unique names
            // Map from original top-level name -> (finalName, folderId, folderPath)
            var topLevelMap = new Dictionary<string, (string finalName, int folderId, string folderPath)>(StringComparer.OrdinalIgnoreCase);

            if (anyHasPath)
            {
                // Gather distinct top-level names from the selection
                var topLevelNames = uploadedFiles
                    .Select(f => (f.FileName ?? "").Replace("/", Path.DirectorySeparatorChar.ToString()).Replace("\\", Path.DirectorySeparatorChar.ToString()))
                    .Select(rel => rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length >= 1)
                    .Select(parts => parts[0])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var rootName in topLevelNames)
                {
                    // Compute a unique folder name under parentPath/parentFolderId for the root of this upload
                    string candidate = rootName;
                    string candidatePath = Path.Combine(parentPath, candidate);
                    int counter = 2;

                    while (Directory.Exists(candidatePath) || await _context.Folders.AnyAsync(f =>
                        f.UserID == userId && f.ParentFolderID == parentFolderId && !f.IsDeleted && f.FolderName == candidate))
                    {
                        candidate = $"{rootName} ({counter++})";
                        candidatePath = Path.Combine(parentPath, candidate);
                    }

                    // Create physical directory if not exists
                    if (!Directory.Exists(candidatePath))
                        Directory.CreateDirectory(candidatePath);

                    // Create or get DB folder for this root
                    var rootFolder = new Folder
                    {
                        FolderName = candidate,
                        ParentFolderID = parentFolderId,
                        UserID = userId,
                        FolderPath = candidatePath,
                        CreatedAt = GetPakistanTime()
                    };
                    _context.Folders.Add(rootFolder);
                    await _context.SaveChangesAsync();

                    topLevelMap[rootName] = (candidate, rootFolder.FolderID, candidatePath);
                }
            }

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
                else if (anyHasPath && parts.Length >= 1)
                {
                    // Use the unique root created for this top-level folder
                    var rootName = parts[0];
                    if (topLevelMap.TryGetValue(rootName, out var rootInfo))
                    {
                        currentParentId = rootInfo.folderId;
                        currentPath = rootInfo.folderPath;
                    }
                }

                // ensure folder structure
                int startIndex = (anyHasPath && parts.Length >= 1) ? 1 : 0; // skip the original top-level; we've mapped it to unique folder
                for (int i = startIndex; i < parts.Length - 1; i++)
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
                            FolderPath = currentPath,
                            CreatedAt = GetPakistanTime()
                        };
                        _context.Folders.Add(folderEntity);
                        await _context.SaveChangesAsync();
                    }

                    currentParentId = folderEntity.FolderID;

                    if (!Directory.Exists(currentPath))
                        Directory.CreateDirectory(currentPath);
                }

                // physical file name (with extension)
                var fileName = parts.Last();
                var destPath = Path.Combine(currentPath, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using (var stream = new FileStream(destPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                // ensure parent folder in DB exists
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
                                FolderPath = userRootPath,
                                CreatedAt = GetPakistanTime()
                            };
                            _context.Folders.Add(userRootFolder);
                            await _context.SaveChangesAsync();
                        }
                        currentParentId = userRootFolder.FolderID;
                    }
                }

                // save file in DB
                var fileSizeMb = Math.Round((decimal)file.Length / (1024 * 1024), 4);
                totalAddedMb += fileSizeMb;

                string fileType = Path.GetExtension(fileName);
                fileType = string.IsNullOrWhiteSpace(fileType)
                    ? (file.ContentType ?? "")
                    : fileType.TrimStart('.').ToLowerInvariant();

                var dbFile = new FileItem
                {
                    FileName = Path.GetFileNameWithoutExtension(fileName),
                    FileSizeMB = fileSizeMb,
                    FolderID = currentParentId.Value,
                    UserID = userId,
                    FilePath = destPath,
                    FileType = fileType,
                    UploadedAt = GetPakistanTime()
                };

                _context.Files.Add(dbFile);
                await _context.SaveChangesAsync();
            }

            // ---------- 10) Update UsedStorageMB ----------
            user.UsedStorageMB += totalAddedMb;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Ok("Folder uploaded successfully with hierarchy.");
        }

        // helper to ensure root folder exists in DB
        private async Task<int> EnsureUserRootFolder(int userId, string userRootPath)
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
                    FolderPath = userRootPath,
                    CreatedAt = GetPakistanTime()
                };
                _context.Folders.Add(userRootFolder);
                await _context.SaveChangesAsync();
            }
            return userRootFolder.FolderID;
        }


        // =========================
        // RENAME FOLDER API
        // =========================
        [HttpPut("rename")]
        public async Task<IActionResult> RenameFolder(int folderId, string newName)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized("User not logged in.");

            if (string.IsNullOrWhiteSpace(newName))
                return BadRequest("Folder name cannot be empty.");

            int userId = int.Parse(userIdStr);

            // 1) Load folder
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);
            if (folder == null)
                return NotFound("Folder not found.");

            // 2) Capture old paths
            string oldFolderPath = folder.FolderPath;                // absolute (as stored in your other APIs)
            string? parentPath = Path.GetDirectoryName(oldFolderPath);
            if (string.IsNullOrEmpty(parentPath))
                return BadRequest("Invalid folder path.");

            // 3) Compute unique new name among siblings (DB + disk)
            string baseName = newName.Trim();
            if (string.IsNullOrWhiteSpace(baseName))
                return BadRequest("Invalid new name.");

            var siblingNames = await _context.Folders
                .Where(f => f.UserID == userId
                         && f.ParentFolderID == folder.ParentFolderID
                         && f.FolderID != folderId
                         && !f.IsDeleted)
                .Select(f => f.FolderName)
                .ToListAsync();

            string finalName = baseName;
            string newFolderPath = Path.Combine(parentPath, finalName);
            int counter = 2;
            while (siblingNames.Any(n => n.Equals(finalName, StringComparison.OrdinalIgnoreCase))
                   || Directory.Exists(newFolderPath))
            {
                finalName = $"{baseName} ({counter++})";
                newFolderPath = Path.Combine(parentPath, finalName);
            }

            // 4) Move physically on disk
            try
            {
                if (!Directory.Exists(parentPath))
                    Directory.CreateDirectory(parentPath);

                if (Directory.Exists(oldFolderPath))
                    Directory.Move(oldFolderPath, newFolderPath);
                else
                    return NotFound("Physical folder not found on disk.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to rename folder on disk: {ex.Message}");
            }

            // 5) Prepare safe prefix matching (boundary = separator)
            string sep = Path.DirectorySeparatorChar.ToString();
            string alt = Path.AltDirectorySeparatorChar.ToString();

            string oldPrefixA = oldFolderPath.EndsWith(sep) ? oldFolderPath : oldFolderPath + sep;
            string oldPrefixB = oldFolderPath.EndsWith(alt) ? oldFolderPath : oldFolderPath + alt;
            string newPrefix = newFolderPath.EndsWith(sep) ? newFolderPath : newFolderPath + sep;

            // 6) Update this folder in DB
            folder.FolderName = finalName;
            folder.FolderPath = newFolderPath;

            // 7) Update ONLY descendant folders (not siblings like "Uploaded Folder (2)")
            var childFolders = await _context.Folders
                .Where(f => f.UserID == userId && !f.IsDeleted &&
                            (f.FolderPath.StartsWith(oldPrefixA) || f.FolderPath.StartsWith(oldPrefixB)))
                .ToListAsync();

            foreach (var child in childFolders)
            {
                if (child.FolderPath.StartsWith(oldPrefixA))
                    child.FolderPath = newPrefix + child.FolderPath.Substring(oldPrefixA.Length);
                else
                    child.FolderPath = newPrefix + child.FolderPath.Substring(oldPrefixB.Length);
            }

            // 8) Update ONLY descendant files (safe boundary with separator)
            var childFiles = await _context.Files
                .Where(f => f.UserID == userId && !f.IsDeleted &&
                            (f.FilePath.StartsWith(oldPrefixA) || f.FilePath.StartsWith(oldPrefixB)))
                .ToListAsync();

            foreach (var fi in childFiles)
            {
                if (fi.FilePath.StartsWith(oldPrefixA))
                    fi.FilePath = newPrefix + fi.FilePath.Substring(oldPrefixA.Length);
                else
                    fi.FilePath = newPrefix + fi.FilePath.Substring(oldPrefixB.Length);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Folder renamed successfully",
                FolderID = folder.FolderID,
                FolderName = folder.FolderName,
                FolderPath = folder.FolderPath,
                ParentFolderID = folder.ParentFolderID,
                CreatedAt = folder.CreatedAt
            });
        }

        // =========================
        // DELETE FOLDER API
        // =========================
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFolder(int folderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // ✅ 1) Get the folder to delete
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);

            if (folder == null)
                return NotFound("Folder not found.");

            string folderPath = folder.FolderPath;

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

            // ✅ 5) Move physically
            string folderName = Path.GetFileName(folderPath);
            string destinationPath = Path.Combine(recycleBinPath, folderName);
            try
            {
                Directory.Move(folderPath, destinationPath);
            }
            catch
            {
                return StatusCode(500, "Error moving folder to recycle bin.");
            }

            // ✅ 6) Mark only this folder, its subfolders and contained files as deleted
            // Get all subfolders (direct + nested)
            var affectedFolders = await _context.Folders
                .Where(f => f.UserID == userId && f.FolderPath.StartsWith(folderPath + Path.DirectorySeparatorChar))
                .ToListAsync();

            // Add the folder itself
            affectedFolders.Add(folder);

            foreach (var f in affectedFolders)
            {
                f.IsDeleted = true;
                // ❌ Don't touch FolderPath (keep original for restore)
            }

            // Get all files inside this folder or its subfolders
            var affectedFiles = await _context.Files
                .Where(file => file.UserID == userId &&
                               (file.FilePath.StartsWith(folderPath + Path.DirectorySeparatorChar) ||
                                file.FilePath.StartsWith(folderPath + Path.AltDirectorySeparatorChar)))
                .ToListAsync();

            foreach (var file in affectedFiles)
            {
                file.IsDeleted = true;
                // ❌ Don't touch FilePath (keep original for restore)
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Folder moved to recycle bin successfully." });
        }

        // =========================
        // RECOVER FOLDER API
        // =========================
        [HttpPut("recover/{folderId}")]
        public async Task<IActionResult> RecoverFolder(int folderId)
        {
            // 0) Auth
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            // 1) Load the target folder (must be deleted)
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && f.IsDeleted);

            if (folder == null)
                return NotFound("Deleted folder not found.");

            string originalFolderPath = folder.FolderPath; // original path (unchanged on delete)
            string folderName = Path.GetFileName(originalFolderPath);
            string originalParent = Path.GetDirectoryName(originalFolderPath)!;

            // 2) Build user root + RecycleBin path
            var userRoot = Path.Combine(_basePath, userId.ToString());
            var recycleBinPath = Path.Combine(userRoot, "RecycleBin");

            if (!Directory.Exists(recycleBinPath))
                return NotFound("Recycle Bin not found for this user.");

            // 3) Locate the folder inside RecycleBin
            string? recycleCurrentPath = null;
            var exactCandidate = Path.Combine(recycleBinPath, folderName);
            if (Directory.Exists(exactCandidate))
            {
                recycleCurrentPath = exactCandidate;
            }
            else
            {
                var candidates = Directory.GetDirectories(recycleBinPath, folderName + "*", SearchOption.TopDirectoryOnly)
                    .Where(d =>
                    {
                        var dn = Path.GetFileName(d)!;
                        if (dn.Equals(folderName, StringComparison.OrdinalIgnoreCase)) return true;
                        return System.Text.RegularExpressions.Regex.IsMatch(
                            dn, "^" + System.Text.RegularExpressions.Regex.Escape(folderName) + @" \(\d+\)$",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    })
                    .OrderByDescending(d => System.IO.Directory.GetLastWriteTime(d))
                    .ToList();

                recycleCurrentPath = candidates.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(recycleCurrentPath) || !Directory.Exists(recycleCurrentPath))
                return NotFound("Folder not found inside Recycle Bin.");

            // 4) Ensure original parent path exists
            if (!Directory.Exists(originalParent))
                Directory.CreateDirectory(originalParent);

            // 5) Resolve name conflict: if folder exists, append (2), (3), etc.
            string targetFolderPath = originalFolderPath;
            string baseName = folderName;
            int counter = 2;
            while (Directory.Exists(targetFolderPath))
            {
                string newName = $"{baseName} ({counter})";
                targetFolderPath = Path.Combine(originalParent, newName);
                counter++;
            }

            string finalFolderName = Path.GetFileName(targetFolderPath);

            // 6) Move physical folder
            try
            {
                Directory.Move(recycleCurrentPath, targetFolderPath);
            }
            catch
            {
                return StatusCode(500, "Failed to move folder back from Recycle Bin.");
            }

            // 7) Cascade DB restore: update paths + set IsDeleted = false
            string sep = Path.DirectorySeparatorChar.ToString();
            string alt = Path.AltDirectorySeparatorChar.ToString();

            string prefixA = originalFolderPath.EndsWith(sep) ? originalFolderPath : originalFolderPath + sep;
            string prefixB = originalFolderPath.EndsWith(alt) ? originalFolderPath : originalFolderPath + alt;

            string newPrefix = targetFolderPath.EndsWith(sep) ? targetFolderPath : targetFolderPath + sep;

            // restore folders
            var foldersToRestore = await _context.Folders
                .Where(f =>
                    f.UserID == userId &&
                    f.IsDeleted &&
                    (f.FolderID == folderId ||
                     f.FolderPath.StartsWith(prefixA) ||
                     f.FolderPath.StartsWith(prefixB)))
                .ToListAsync();

            foreach (var fld in foldersToRestore)
            {
                fld.IsDeleted = false;

                // adjust path if needed
                if (fld.FolderPath == originalFolderPath)
                {
                    fld.FolderPath = targetFolderPath;

                    fld.FolderName = finalFolderName;
                }
                else if (fld.FolderPath.StartsWith(prefixA))
                {
                    fld.FolderPath = fld.FolderPath.Replace(prefixA, newPrefix);
                }
                else if (fld.FolderPath.StartsWith(prefixB))
                {
                    fld.FolderPath = fld.FolderPath.Replace(prefixB, newPrefix);
                }
            }

            // restore files
            var filesToRestore = await _context.Files
                .Where(file =>
                    file.UserID == userId &&
                    file.IsDeleted &&
                    (file.FilePath.StartsWith(prefixA) || file.FilePath.StartsWith(prefixB)))
                .ToListAsync();

            foreach (var fl in filesToRestore)
            {
                fl.IsDeleted = false;
                if (fl.FilePath.StartsWith(prefixA))
                    fl.FilePath = fl.FilePath.Replace(prefixA, newPrefix);
                else if (fl.FilePath.StartsWith(prefixB))
                    fl.FilePath = fl.FilePath.Replace(prefixB, newPrefix);
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Folder restored successfully as '{finalFolderName}'." });
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
            // Expose Content-Disposition so frontend can read filename
            Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
            return File(zipBytes, "application/zip", zipFileName);
        }

        // =========================
        // GET FOLDER DETAILS API
        // =========================
        //[HttpGet("details")]
        //public async Task<IActionResult> GetFolderDetails([FromQuery] int folderId)
        //{
        //    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? HttpContext.Session.GetString("UserID");
        //    if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        //    int userId = int.Parse(userIdStr);

        //    var folder = await _context.Folders
        //        .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);

        //    if (folder == null) return NotFound("Folder not found.");

        //    // ✅ Subfolders
        //    var subfolders = await _context.Folders
        //        .Where(f => f.ParentFolderID == folderId && f.UserID == userId && !f.IsDeleted)
        //        .Select(f => new
        //        {
        //            f.FolderName,
        //            f.CreatedAt
        //        })
        //        .ToListAsync();

        //    // ✅ Files
        //    var files = await _context.Files
        //        .Where(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted)
        //        .Select(f => new
        //        {
        //            f.FileName,
        //            f.FileSizeMB,
        //            f.FileType,
        //            f.UploadedAt
        //        })
        //        .ToListAsync();

        //    // ✅ Calculate folder size
        //    var totalSizeMB = files.Sum(f => f.FileSizeMB);

        //    return Ok(new
        //    {
        //        FolderID = folder.FolderID,
        //        FolderName = folder.FolderName,
        //        CreatedAt = folder.CreatedAt,
        //        TotalFiles = files.Count,
        //        TotalFolders = subfolders.Count,
        //        TotalSizeMB = totalSizeMB,
        //        ParentFolderID = folder.ParentFolderID
        //    });
        //}

        // =========================
        // HELPER METHODS
        // =========================
        private async Task UpdateSubfolderPaths(int userId, string oldPath, string newPath)
        {
            // Update all subfolders
            var subfolders = await _context.Folders
                .Where(f => f.UserID == userId && f.FolderPath.StartsWith(oldPath + "\\"))
                .ToListAsync();

            foreach (var subfolder in subfolders)
            {
                subfolder.FolderPath = subfolder.FolderPath.Replace(oldPath, newPath);
            }

            // Update all files in this folder and subfolders
            var files = await _context.Files
                .Where(f => f.UserID == userId && f.FilePath.StartsWith(oldPath + "\\"))
                .ToListAsync();

            foreach (var file in files)
            {
                file.FilePath = file.FilePath.Replace(oldPath, newPath);
            }
        }

        private async Task SoftDeleteFolderRecursively(int userId, int folderId)
        {
            // Get the folder
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId);

            if (folder != null)
            {
                folder.IsDeleted = true;

                // Delete all files in this folder
                var files = await _context.Files
                    .Where(f => f.FolderID == folderId && f.UserID == userId)
                    .ToListAsync();

                foreach (var file in files)
                {
                    file.IsDeleted = true;
                }

                // Recursively delete all subfolders
                var subfolders = await _context.Folders
                    .Where(f => f.ParentFolderID == folderId && f.UserID == userId)
                    .ToListAsync();

                foreach (var subfolder in subfolders)
                {
                    await SoftDeleteFolderRecursively(userId, subfolder.FolderID);
                }
            }
        }

        // =========================
        // PERMANENT DELETE FOLDER API
        // =========================
        [HttpDelete("permanent-delete")]
        public async Task<IActionResult> PermanentDeleteFolder([FromQuery] int folderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && f.IsDeleted);

            if (folder == null)
                return NotFound("Folder not found in recycle bin.");

            try
            {
                // Sum sizes of all deleted files under this folder (recursively) using path prefix
                string sep = Path.DirectorySeparatorChar.ToString();
                string alt = Path.AltDirectorySeparatorChar.ToString();
                string prefixA = folder.FolderPath.EndsWith(sep) ? folder.FolderPath : folder.FolderPath + sep;
                string prefixB = folder.FolderPath.EndsWith(alt) ? folder.FolderPath : folder.FolderPath + alt;

                decimal totalRemovedMb = (await _context.Files
                    .Where(f => f.UserID == userId && f.IsDeleted &&
                        (f.FilePath.StartsWith(prefixA) || f.FilePath.StartsWith(prefixB)))
                    .SumAsync(f => (decimal?)f.FileSizeMB)) ?? 0m;

                await PermanentDeleteFolderRecursively(userId, folderId);

                // Update user's used storage after deletion
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
                if (user != null && totalRemovedMb > 0)
                {
                    user.UsedStorageMB = Math.Max(0, user.UsedStorageMB - totalRemovedMb);
                    _context.Users.Update(user);
                    await _context.SaveChangesAsync();
                }
                return Ok(new { message = "Folder permanently deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error permanently deleting folder: {ex.Message}");
            }
        }

        private async Task PermanentDeleteFolderRecursively(int userId, int folderId)
        {
            // Delete all files in this folder
            var files = await _context.Files
                .Where(f => f.FolderID == folderId && f.UserID == userId && f.IsDeleted)
                .ToListAsync();

            foreach (var file in files)
            {
                var userRoot = Path.Combine(_basePath, userId.ToString());
                var recycleBinDir = Path.Combine(userRoot, "RecycleBin");
                var recycleCandidate = Path.Combine(recycleBinDir, Path.GetFileName(file.FilePath));

                if (System.IO.File.Exists(recycleCandidate))
                {
                    System.IO.File.Delete(recycleCandidate);
                }
                else if (System.IO.File.Exists(file.FilePath))
                {
                    System.IO.File.Delete(file.FilePath);
                }

                _context.Files.Remove(file);
            }

            // Get subfolders and delete recursively
            var subfolders = await _context.Folders
                .Where(f => f.ParentFolderID == folderId && f.UserID == userId && f.IsDeleted)
                .ToListAsync();

            foreach (var subfolder in subfolders)
            {
                await PermanentDeleteFolderRecursively(userId, subfolder.FolderID);
            }

            // Finally, delete the folder itself
            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId && f.IsDeleted);

            if (folder != null)
            {
                var userRoot = Path.Combine(_basePath, userId.ToString());
                var recycleBinDir = Path.Combine(userRoot, "RecycleBin");
                var recycleCandidate = Path.Combine(recycleBinDir, Path.GetFileName(folder.FolderPath));

                if (Directory.Exists(recycleCandidate))
                {
                    Directory.Delete(recycleCandidate, true);
                }
                else if (Directory.Exists(folder.FolderPath))
                {
                    Directory.Delete(folder.FolderPath, true);
                }

                _context.Folders.Remove(folder);
            }

            await _context.SaveChangesAsync();
        }


        // =========================
        // FOLDER DETAILS API
        // =========================
        [HttpGet("details")]
        public async Task<IActionResult> GetFolderDetails([FromQuery] int folderId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);

            var folder = await _context.Folders
                .FirstOrDefaultAsync(f => f.FolderID == folderId && f.UserID == userId);

            if (folder == null)
                return NotFound("Folder not found.");

            // Get counts of files and subfolders
            var fileCount = await _context.Files
                .CountAsync(f => f.FolderID == folderId && f.UserID == userId && !f.IsDeleted);

            var subfolderCount = await _context.Folders
                .CountAsync(f => f.ParentFolderID == folderId && f.UserID == userId && !f.IsDeleted);

            return Ok(new
            {
                FolderID = folder.FolderID,
                FolderName = folder.FolderName,
                FolderPath = folder.FolderPath,
                ParentFolderID = folder.ParentFolderID,
                CreatedAt = folder.CreatedAt,
                IsDeleted = folder.IsDeleted,
                FileCount = fileCount,
                SubfolderCount = subfolderCount
            });
        }
    }
}