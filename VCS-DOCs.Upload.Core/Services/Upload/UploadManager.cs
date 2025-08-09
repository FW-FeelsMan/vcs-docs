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

        public async Task<FileUploadSessionModel> StartOrContinueSessionAsync(string shortUserId, string originalFileName, string fileHash, long fileSize, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var existing = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                var existDir = _pathValidator.GetChunkDirectory(shortUserId, existing.FileId);
                TryClearStoppedFlag(existDir);
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
            TryClearStoppedFlag(chunkDir);

            return model;
        }

        public async Task RestartUploadAsync(string shortUserId, string originalFileName, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var active = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (active != null)
            {
                var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, active.FileId);
                if (Directory.Exists(chunkDir))
                {
                    try { Directory.Delete(chunkDir, true); } catch { }
                }
                active.Status = "deleted";
                active.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task<(bool ok, string message, int nextExpectedIndex, Guid sessionId)> HandleChunkUploadAsync(
            string shortUserId, IFormFile chunk, string fileHash, int chunkIndex, int totalChunks, long fileSize, string originalFileName, CancellationToken ct = default)
        {
            var session = await StartOrContinueSessionAsync(shortUserId, originalFileName, fileHash, fileSize, ct);
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            Directory.CreateDirectory(chunkDir);
            TryClearStoppedFlag(chunkDir);

            // Save chunk
            var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex:D8}");
            using (var s = chunk.OpenReadStream())
            using (var fs = File.Create(chunkPath))
            {
                await s.CopyToAsync(fs, ct);
            }

            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // If not last chunk — tell client the next expected
            var existsCount = Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly).Count();
            var next = Math.Min(existsCount, totalChunks);
            if (existsCount < totalChunks)
            {
                return (true, "chunk saved", next, session.FileId);
            }

            // Assemble final file
            var filesRoot = _paths.GetFileRoot(shortUserId);
            Directory.CreateDirectory(filesRoot);
            var versionFolder = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), session.Version);
            Directory.CreateDirectory(versionFolder);
            var finalFilePath = _paths.GetFilePath(shortUserId, session.FileGroupId.ToString(), session.Version, originalFileName);

            try
            {
                using (var fs = File.Create(finalFilePath))
                {
                    for (int i = 0; i < totalChunks; i++)
                    {
                        var p = Path.Combine(chunkDir, $"chunk_{i:D8}");
                        using (var rs = File.OpenRead(p))
                        {
                            await rs.CopyToAsync(fs, ct);
                        }
                        try { File.Delete(p); } catch { }
                    }
                }
            }
            catch (IOException)
            {
                try { if (File.Exists(finalFilePath)) File.Delete(finalFilePath); } catch { }
                try { if (Directory.Exists(versionFolder) && Directory.GetFiles(versionFolder).Length == 0) Directory.Delete(versionFolder); } catch { }
                return (false, "insufficient_storage", next, session.FileId);
            }

            // Hash check (skip for fp: but persist real hash)
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(finalFilePath))
            {
                var finalHash = Convert.ToHexString(md5.ComputeHash(fs));
                bool isFingerprint = fileHash != null && fileHash.StartsWith("fp:", StringComparison.OrdinalIgnoreCase);
                if (!isFingerprint && !finalHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(finalFilePath); } catch { }
                    try { if (Directory.Exists(versionFolder) && Directory.GetFiles(versionFolder).Length == 0) Directory.Delete(versionFolder); } catch { }
                    return (false, "hash mismatch", next, session.FileId);
                }
                if (isFingerprint)
                {
                    session.FileHash = finalHash;
                }
            }

            session.Status = "complete";
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            if (Directory.Exists(chunkDir))
            {
                try { Directory.Delete(chunkDir, true); } catch { }
            }

            return (true, "completed", totalChunks, session.FileId);
        }

        public async Task<List<VersionDto>> GetAllVersionsAsync(string shortUserId, string originalFileName, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);

            var rows = await _db.FileUploadSessions
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.OriginalFileName == originalFileName && x.Status == "complete")
                .OrderByDescending(x => x.Version)
                .Select(x => new { x.Version, x.UpdatedAt, x.FileSize })
                .ToListAsync(ct);

            return rows.Select(x => new VersionDto
            {
                Version = x.Version,
                UploadedAt = new DateTimeOffset(DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)),
                FileSize = x.FileSize
            }).ToList();
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
                        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(latest.UpdatedAt, DateTimeKind.Utc)),
                        LatestVersion = latest.Version,
                        Versions = g
                            .OrderByDescending(x => x.Version)
                            .Select(x => new VersionDto
                            {
                                Version = x.Version,
                                UploadedAt = new DateTimeOffset(DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)),
                                FileSize = x.FileSize
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
                        if (tail.StartsWith("0x"))
                        {
                            var hex = tail.Substring(2);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var idxHex))
                                uploaded.Add(idxHex);
                        }
                        else
                        {
                            if (int.TryParse(tail, out var idxDec)) uploaded.Add(idxDec);
                        }
                    }
                }
            }
            return (session.FileId, uploaded);
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
            public List<int> Uploaded { get; set; } = new List<int>();
            public long UploadedBytes
            {
                get; set;
            }

            // IMPORTANT: DateTimeOffset
            public DateTimeOffset UpdatedAt
            {
                get; set;
            }

            public bool Stopped
            {
                get; set;
            }
        }

        public async Task<ActiveUploadInfo?> GetActiveUploadForUserAsync(string shortUserId, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var session = await _db.FileUploadSessions
                .Where(x => x.UserId == userId && x.Status == "uploading")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (session == null) return null;

            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            var uploaded = new List<int>();
            long uploadedBytes = 0;

            if (Directory.Exists(chunkDir))
            {
                foreach (var p in Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(p);
                    if (name.StartsWith("chunk_"))
                    {
                        var tail = name.Substring("chunk_".Length);
                        if (tail.StartsWith("0x"))
                        {
                            var hex = tail.Substring(2);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var idxHex))
                            {
                                uploaded.Add(idxHex);
                                try { uploadedBytes += new FileInfo(p).Length; } catch { }
                            }
                        }
                        else
                        {
                            if (int.TryParse(tail, out var idxDec))
                            {
                                uploaded.Add(idxDec);
                                try { uploadedBytes += new FileInfo(p).Length; } catch { }
                            }
                        }
                    }
                }
            }

            return new ActiveUploadInfo
            {
                SessionId = session.FileId,
                FileGroupId = session.FileGroupId,
                FileName = session.OriginalFileName,
                FileHash = session.FileHash,
                Version = session.Version,
                FileSize = session.FileSize,
                Uploaded = uploaded.OrderBy(x => x).ToList(),
                UploadedBytes = uploadedBytes,

                // force UTC
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(session.UpdatedAt, DateTimeKind.Utc)),
                Stopped = IsStopped(chunkDir)
            };
        }

        public async Task MarkStoppedAsync(string shortUserId, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var session = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (session == null) return;
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            try
            {
                Directory.CreateDirectory(chunkDir);
                File.WriteAllText(Path.Combine(chunkDir, ".stopped"), "1");
            }
            catch { }
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        public async Task TouchActiveAsync(string shortUserId, string fileHash, CancellationToken ct = default)
        {
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var session = await GetActiveUploadingSessionAsync(userId, fileHash, ct);
            if (session == null) return;
            var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
            TryClearStoppedFlag(chunkDir);
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private static bool IsStopped(string chunkDir)
        {
            try { return File.Exists(Path.Combine(chunkDir, ".stopped")); } catch { return false; }
        }

        private static void TryClearStoppedFlag(string chunkDir)
        {
            try
            {
                var f = Path.Combine(chunkDir, ".stopped");
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
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
            catch { }

            return true;
        }
    }
}