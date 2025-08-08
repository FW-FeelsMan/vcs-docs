using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Infrastructure.Services.Storage;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core.Models;

namespace VCS_DOCs.Upload.Core
{
    public class UploadManager
    {
        private readonly IUploadDbContext _db;
        private readonly IFileStorageService _storage;
        private readonly FilePathValidator _pathValidator;
        private readonly UserStoragePaths _paths;
        private readonly IUserInfoProvider _userInfoProvider;

        public UploadManager(IUploadDbContext db, IFileStorageService storage, FilePathValidator pathValidator, UserStoragePaths paths, IUserInfoProvider userInfoProvider)
        {
            _db = db;
            _storage = storage;
            _pathValidator = pathValidator;
            _paths = paths;
            _userInfoProvider = userInfoProvider;
        }

        public async Task<(long usedBytes, long tempBytes, long limitBytes)> GetStorageStatsAsync(string shortUserId, CancellationToken ct = default)
        {
            var used = await _storage.GetUsedBytesAsync(shortUserId);
            var temp = await _storage.GetTempBytesAsync(shortUserId);
            var limit = await _userInfoProvider.GetUserStorageLimitAsync(shortUserId);
            return (used, temp, limit);
        }

        public async Task<FileUploadSessionModel?> GetActiveUploadingSessionAsync(string userId, string fileHash, CancellationToken ct = default)
        {
            return await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.FileHash == fileHash && x.Status == "uploading")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<bool> HasCompletedVersionAsync(string userId, string originalFileName, CancellationToken ct = default)
        {
            return await _db.FileUploadSessions
                .AnyAsync(x => x.UserId == userId && x.OriginalFileName == originalFileName && x.Status == "complete", ct);
        }

        public class ActiveUploadInfo
        {
            public Guid SessionId
            {
                get; set;
            }
            public Guid FileGroupId
            {
                get; set;
            }
            public string FileName { get; set; } = "";
            public string FileHash { get; set; } = "";
            public int Version
            {
                get; set;
            }
            public long FileSize
            {
                get; set;
            }
            public DateTime UpdatedAt
            {
                get; set;
            }
            public List<int> Uploaded { get; set; } = new List<int>();
        }

        public async Task<ActiveUploadInfo?> GetActiveUploadForUserAsync(string shortUserId, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var freshAfter = DateTime.UtcNow.AddHours(-24);
            var s = await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.Status == "uploading" && x.UpdatedAt >= freshAfter)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (s == null) return null;
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, s.FileId);
            var uploaded = new List<int>();
            if (Directory.Exists(chunkDir))
            {
                foreach (var p in Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(p);
                    if (name.StartsWith("chunk_"))
                    {
                        var tail = name.Substring("chunk_".Length);
                        if (int.TryParse(tail, out var idx)) uploaded.Add(idx);
                    }
                }
            }
            uploaded.Sort();
            return new ActiveUploadInfo
            {
                SessionId = s.FileId,
                FileGroupId = s.FileGroupId,
                FileName = s.OriginalFileName,
                FileHash = s.FileHash,
                Version = s.Version,
                FileSize = s.FileSize,
                UpdatedAt = s.UpdatedAt,
                Uploaded = uploaded
            };
        }

        public async Task RestartUploadAsync(string shortUserId, string originalFileName, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var active = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (active != null)
            {
                var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, active.FileId);
                SafeDeleteDirectory(chunkDir);
                active.Status = "deleted";
                active.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task CleanupTempByHashAsync(string shortUserId, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var active = await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.FileHash == fileHash && x.Status == "uploading")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (active != null)
            {
                var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, active.FileId);
                SafeDeleteDirectory(chunkDir);
                active.Status = "deleted";
                active.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task<(bool ok, string message, int nextExpectedIndex, Guid sessionId)> HandleChunkUploadAsync(string shortUserId, IFormFile chunk, string fileHash, int chunkIndex, int totalChunks, long fileSize, string originalFileName, CancellationToken ct = default)
        {
            var session = await StartOrContinueSessionAsync(shortUserId, originalFileName, fileHash, fileSize, ct);
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            Directory.CreateDirectory(chunkDir);
            var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex:D8}");
            using (var s = chunk.OpenReadStream())
            using (var fs = File.Create(chunkPath))
            {
                await s.CopyToAsync(fs, ct);
            }
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            var existsCount = Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly).Count();
            var next = Math.Min(existsCount, totalChunks);
            if (existsCount < totalChunks)
            {
                return (true, "chunk saved", next, session.FileId);
            }
            var filesRoot = _paths.GetFileRoot(shortUserId);
            Directory.CreateDirectory(filesRoot);
            var versionFolder = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), session.Version);
            Directory.CreateDirectory(versionFolder);
            var finalFilePath = _paths.GetFilePath(shortUserId, session.FileGroupId.ToString(), session.Version, originalFileName);
            using (var fs = File.Create(finalFilePath))
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var p = Path.Combine(chunkDir, $"chunk_{i:D8}");
                    using var rs = File.OpenRead(p);
                    await rs.CopyToAsync(fs, ct);
                }
                await fs.FlushAsync(ct);
            }
            var finalHash = await ComputeMD5HexAsync(finalFilePath, ct);
            if (!finalHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase))
            {
                DeleteFileAndParentIfEmpty(finalFilePath, versionFolder);
                return (false, "hash mismatch", next, session.FileId);
            }
            session.Status = "complete";
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            SafeDeleteDirectory(chunkDir);
            return (true, "completed", totalChunks, session.FileId);
        }

        public async Task<FileUploadSessionModel> StartOrContinueSessionAsync(string shortUserId, string originalFileName, string fileHash, long fileSize, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var existing = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return existing;
            }
            var latestComplete = await _db.FileUploadSessions
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.OriginalFileName == originalFileName && x.Status == "complete")
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(ct);
            var nextVersion = (latestComplete?.Version ?? 0) + 1;
            var fileGroupId = latestComplete?.FileGroupId ?? Guid.NewGuid();
            var model = new FileUploadSessionModel
            {
                FileId = Guid.NewGuid(),
                FileGroupId = fileGroupId,
                UserId = userId,
                OriginalFileName = originalFileName,
                FileHash = fileHash,
                FileSize = fileSize,
                Status = "uploading",
                UpdatedAt = DateTime.UtcNow,
                Version = nextVersion
            };
            _db.FileUploadSessions.Add(model);
            await _db.SaveChangesAsync(ct);
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, model.FileId);
            Directory.CreateDirectory(chunkDir);
            return model;
        }

        public async Task<List<VersionDto>> GetAllVersionsAsync(string shortUserId, string originalFileName, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var list = await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.OriginalFileName == originalFileName && x.Status == "complete")
                .OrderByDescending(x => x.Version)
                .Select(x => new VersionDto
                {
                    Version = x.Version,
                    UploadedAt = x.UpdatedAt
                })
                .ToListAsync(ct);
            return list;
        }

        public async Task<List<UserFileDto>> GetAllUserFilesAsync(string shortUserId, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var sessions = await _db.FileUploadSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.Status == "complete")
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync(ct);
            var grouped = sessions
                .GroupBy(s => s.OriginalFileName)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.Version).First();
                    return new UserFileDto
                    {
                        FileId = latest.FileId,
                        FileGroupId = latest.FileGroupId,
                        FileName = g.Key,
                        FileSize = latest.FileSize,
                        UpdatedAt = latest.UpdatedAt,
                        LatestVersion = latest.Version,
                        Versions = g
                            .OrderByDescending(x => x.Version)
                            .Select(x => new VersionDto
                            {
                                Version = x.Version,
                                UploadedAt = x.UpdatedAt
                            })
                            .ToList()
                    };
                })
                .OrderByDescending(f => f.UpdatedAt)
                .ToList();
            return grouped;
        }

        public async Task<(Guid SessionId, List<int> Uploaded)> GetOngoingUploadAsync(string shortUserId, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var session = await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.FileHash == fileHash && x.Status == "uploading")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (session == null) return (Guid.Empty, new List<int>());
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            var uploaded = new List<int>();
            if (Directory.Exists(chunkDir))
            {
                foreach (var p in Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(p);
                    if (name.StartsWith("chunk_"))
                    {
                        var tail = name.Substring("chunk_".Length);
                        if (int.TryParse(tail, out var idx)) uploaded.Add(idx);
                    }
                }
            }
            return (session.FileId, uploaded);
        }

        public async Task<FileContentResultModel?> GetFileVersionAsync(string shortUserId, Guid fileGroupId, int version)
        {
            var session = await _db.FileUploadSessions
                .FirstOrDefaultAsync(s => s.UserId == _userInfoProvider.ResolveFullUserId(shortUserId) && s.FileGroupId == fileGroupId && s.Version == version && s.Status == "complete");
            if (session == null) return null;
            var content = await _storage.ReadFileAsync(shortUserId, session.FileGroupId, session.Version, session.OriginalFileName);
            return new FileContentResultModel
            {
                Content = content,
                FileName = session.OriginalFileName
            };
        }

        public async Task<bool> DeleteFileVersionAsync(string shortUserId, Guid fileGroupId, int version)
        {
            var session = await _db.FileUploadSessions
                .FirstOrDefaultAsync(s => s.UserId == _userInfoProvider.ResolveFullUserId(shortUserId) && s.FileGroupId == fileGroupId && s.Version == version && s.Status == "complete");
            if (session == null) return true;
            _db.FileUploadSessions.Remove(session);
            await _db.SaveChangesAsync();
            await _storage.DeleteFileAsync(shortUserId, session.FileGroupId, session.Version, session.OriginalFileName);
            var versionDir = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), version);
            var fileGroupDir = Path.Combine(_paths.GetFileRoot(shortUserId), session.FileGroupId.ToString());
            try
            {
                var metaPath = Path.Combine(versionDir, "meta.json");
                if (File.Exists(metaPath)) File.Delete(metaPath);
                if (Directory.Exists(versionDir) && Directory.GetFiles(versionDir).Length == 0) Directory.Delete(versionDir);
                if (Directory.Exists(fileGroupDir) && Directory.GetDirectories(fileGroupDir).Length == 0) Directory.Delete(fileGroupDir);
            }
            catch
            {
            }
            return true;
        }

        private static async Task<string> ComputeMD5HexAsync(string path, CancellationToken ct)
        {
            using var md5 = MD5.Create();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            var hash = await md5.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash);
        }

        private static void DeleteFileAndParentIfEmpty(string filePath, string parentDir)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(60);
                }
            }
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Directory.Exists(parentDir) && Directory.GetFiles(parentDir).Length == 0 && Directory.GetDirectories(parentDir).Length == 0) Directory.Delete(parentDir, true);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(60);
                }
            }
        }

        private static void SafeDeleteDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(dir, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(60);
                }
            }
        }
    }
}