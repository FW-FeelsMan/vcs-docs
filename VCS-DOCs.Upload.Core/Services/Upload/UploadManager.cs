using System.Security.Cryptography;
using ClamAV.Net.Client; 
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; 
using Microsoft.Extensions.Logging;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Infrastructure.Services.Storage;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core.Models;
using VCS_DOCs.Upload.Core.Services.Antivirus;

namespace VCS_DOCs.Upload.Core
{
    public class UploadManager
    {
        private readonly IUploadDbContext _db;
        private readonly IFileStorageService _storage;
        private readonly FilePathValidator _pathValidator;
        private readonly UserStoragePaths _paths;
        private readonly IUserInfoProvider _userInfoProvider;
        private readonly IAntivirusScanner _av;
        private readonly IConfiguration _cfg;
        private readonly ILogger<UploadManager>? _log;

        public UploadManager(
            IUploadDbContext db,
            IFileStorageService storage,
            FilePathValidator pathValidator,
            UserStoragePaths paths,
            IUserInfoProvider userInfoProvider,
            IAntivirusScanner av,          // <--- вместо IClamAvClient
            IConfiguration cfg
        )
        {
            _db = db;
            _storage = storage;
            _pathValidator = pathValidator;
            _paths = paths;
            _userInfoProvider = userInfoProvider;
            _av = av;                       // <---
            _cfg = cfg;
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

            // Next expected
            var existsCount = Directory.EnumerateFiles(chunkDir, "chunk_*", SearchOption.TopDirectoryOnly).Count();
            var next = Math.Min(existsCount, totalChunks);
            if (existsCount < totalChunks)
            {
                return (true, "chunk saved", next, session.FileId);
            }

            // ---- All chunks received: antivirus scan via AMSI (or other IAntivirusScanner) ----
            var chunkFiles = Enumerable.Range(0, totalChunks)
                .Select(i => Path.Combine(chunkDir, $"chunk_{i:D8}"))
                .ToArray();

            var timeoutMs = CfgInt("Antivirus:TimeoutMs", 30000);
            using (var concatForScan = new ConcatenatedReadStream(chunkFiles))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeoutMs);
                var verdict = await _av.ScanAsync(concatForScan, originalFileName, cts.Token);

                if (verdict == ScanVerdict.Infected)
                {
                    session.Status = "deleted";
                    session.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    try { Directory.Delete(chunkDir, true); } catch { }
                    return (false, "infected", totalChunks, session.FileId);
                }
                if (verdict == ScanVerdict.Unavailable || verdict == ScanVerdict.Error)
                {
                    if (CfgBool("Antivirus:BlockWhenNoAV", true))
                    {
                        // Блокируем — на фронте это 503 → «временная недоступность антивируса»
                        return (false, "av_unavailable", totalChunks, session.FileId);
                    }
                    // иначе пропускаем без скана
                }
            }

            // Full MD5 without assembling (read concatenated stream)
            string? computedMd5 = null;
            using (var md5 = MD5.Create())
            using (var concatForHash = new ConcatenatedReadStream(chunkFiles))
            {
                var hash = md5.ComputeHash(concatForHash);
                computedMd5 = Convert.ToHexString(hash);
            }

            bool isFingerprint = fileHash != null && fileHash.StartsWith("fp:", StringComparison.OrdinalIgnoreCase);
            if (!isFingerprint && !string.Equals(computedMd5, fileHash, StringComparison.OrdinalIgnoreCase))
            {
                // bad hash
                try { Directory.Delete(chunkDir, true); } catch { }
                session.Status = "deleted";
                session.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return (false, "hash mismatch", totalChunks, session.FileId);
            }
            if (isFingerprint && computedMd5 != null)
            {
                session.FileHash = computedMd5;
            }

            // ---- LAZY materialization:
            // move chunks into the version folder and DO NOT assemble final file
            var versionFolder = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), session.Version);
            Directory.CreateDirectory(versionFolder);

            foreach (var p in chunkFiles)
            {
                var dest = Path.Combine(versionFolder, Path.GetFileName(p));
                try
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(p, dest);
                }
                catch
                {
                    // fallback copy+delete
                    try { File.Copy(p, dest, overwrite: true); } catch { }
                    try { File.Delete(p); } catch { }
                }
            }
            // cleanup chunk temp dir
            try { Directory.Delete(chunkDir, true); } catch { }

            // mark complete
            session.Status = "complete";
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return (true, "completed", totalChunks, session.FileId);
        }
        public async Task<(string ownerShort, FileUploadSessionModel session)?> FindAnyCompletedByGroupVersionAsync(
            Guid fileGroupId, int version, CancellationToken ct = default)
        {
            var s = await _db.FileUploadSessions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.FileGroupId == fileGroupId
                                          && x.Version == version
                                          && x.Status == "complete", ct);
            if (s == null) return null;
            var ownerShort = s.UserId.Replace("-", "").Substring(0, 8);
            return (ownerShort, s);
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
                    UploadedAt = x.UpdatedAt,
                    FileSize = x.FileSize
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
                                UploadedAt = x.UpdatedAt,
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
            public DateTime UpdatedAt
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
                UpdatedAt = session.UpdatedAt,
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
        // ---- Helpers: config without Microsoft.Extensions.Configuration.Binder
        private bool CfgBool(string key, bool defValue)
        {
            try
            {
                var s = _cfg[key];
                if (bool.TryParse(s, out var b)) return b;
                if (int.TryParse(s, out var i)) return i != 0;
                return defValue;
            }
            catch { return defValue; }
        }

        private int CfgInt(string key, int defValue)
        {
            try
            {
                var s = _cfg[key];
                return int.TryParse(s, out var i) ? i : defValue;
            }
            catch { return defValue; }
        }

        // ---- Helper: make ClamAV ScanResult decision robust across libs/versions
        private static bool IsScanClean(object scan)
        {
            if (scan == null) return false;
            var t = scan.GetType();

            // 1) Явные bool "infected"-флаги
            foreach (var name in new[] { "Infected", "IsInfected", "HasInfection", "HasVirus", "IsVirusFound" })
            {
                var p = t.GetProperty(name);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    var infected = (bool)(p.GetValue(scan) ?? false);
                    return !infected;
                }
            }

            // 2) Явные bool "ок/чисто"
            foreach (var name in new[] { "IsSafe", "Ok", "Success", "Clean", "IsOk" })
            {
                var p = t.GetProperty(name);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    var ok = (bool)(p.GetValue(scan) ?? false);
                    return ok;
                }
            }

            // 3) Название сигнатуры/вируса
            foreach (var name in new[] { "MalwareName", "Signature", "VirusName" })
            {
                var p = t.GetProperty(name);
                if (p != null)
                {
                    var s = p.GetValue(scan)?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return false; // есть имя — заражено
                    return true; // пусто — чисто
                }
            }

            // 4) Enum/строковый Status
            var statusProp = t.GetProperty("Status");
            if (statusProp != null)
            {
                var statusText = statusProp.GetValue(scan)?.ToString() ?? "";
                if (statusText.IndexOf("FOUND", StringComparison.OrdinalIgnoreCase) >= 0
                 || statusText.IndexOf("INFECT", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (statusText.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0
                 || statusText.IndexOf("CLEAN", StringComparison.OrdinalIgnoreCase) >= 0
                 || statusText.IndexOf("PASSED", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // 5) Fallback: ToString()
            var txt = scan.ToString() ?? "";
            if (txt.IndexOf("FOUND", StringComparison.OrdinalIgnoreCase) >= 0
             || txt.IndexOf("INFECT", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (txt.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0
             || txt.IndexOf("CLEAN", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // По умолчанию считаем "не ок", чтобы не пропустить
            return false;
        }

        // ========= NEW: Streamed open (assembled file OR lazy chunks) =========
        public sealed class FileOpenResult
        {
            public required Stream Stream
            {
                get; init;
            }
            public required string FileName
            {
                get; init;
            }
            public long? Length
            {
                get; init;
            }
        }

        public async Task<FileOpenResult?> OpenFileVersionStreamAsync(string shortUserId, Guid fileGroupId, int version, CancellationToken ct = default)
        {
            var session = await _db.FileUploadSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.UserId == _userInfoProvider.ResolveFullUserId(shortUserId)
                    && s.FileGroupId == fileGroupId
                    && s.Version == version
                    && s.Status == "complete", ct);

            if (session == null) return null;

            var finalPath = _paths.GetFilePath(shortUserId, session.FileGroupId.ToString(), session.Version, session.OriginalFileName);
            if (File.Exists(finalPath))
            {
                var fs = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new FileOpenResult { Stream = fs, FileName = session.OriginalFileName, Length = fs.Length };
            }

            var versionDir = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), version);
            if (!Directory.Exists(versionDir)) return null;

            var chunkFiles = Directory.EnumerateFiles(versionDir, "chunk_*", SearchOption.TopDirectoryOnly)
                                      .OrderBy(p => p, StringComparer.Ordinal)
                                      .ToArray();
            if (chunkFiles.Length == 0) return null;

            long? totalLen = 0;
            foreach (var p in chunkFiles)
            {
                try { totalLen += new FileInfo(p).Length; } catch { totalLen = null; break; }
            }

            var concat = new ConcatenatedReadStream(chunkFiles); // caller disposes via File()
            return new FileOpenResult { Stream = concat, FileName = session.OriginalFileName, Length = totalLen };
        }

        public async Task<FileContentResultModel?> GetFileVersionAsync(string shortUserId, Guid fileGroupId, int version)
        {
            // kept for backward compatibility (not used by controller after patch)
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

            // delete assembled file (if any)
            await _storage.DeleteFileAsync(shortUserId, session.FileGroupId, session.Version, session.OriginalFileName);

            // also delete chunked version folder (lazy)
            var versionDir = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), version);
            try
            {
                if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
                var fileGroupDir = Path.Combine(_paths.GetFileRoot(shortUserId), session.FileGroupId.ToString());
                if (Directory.Exists(fileGroupDir) && Directory.GetDirectories(fileGroupDir).Length == 0 && Directory.GetFiles(fileGroupDir).Length == 0)
                    Directory.Delete(fileGroupDir);
            }
            catch { }

            return true;
        }

        // ========= NEW: simple concat stream over chunk files =========
        private sealed class ConcatenatedReadStream : Stream
        {
            private readonly Queue<string> _paths;
            private FileStream? _current;

            public ConcatenatedReadStream(IEnumerable<string> paths) => _paths = new Queue<string>(paths);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException(); set => throw new NotSupportedException();
            }

            private void EnsureCurrent()
            {
                while (_current == null || _current.Position >= _current.Length)
                {
                    _current?.Dispose();
                    if (_paths.Count == 0) { _current = null; break; }
                    _current = new FileStream(_paths.Dequeue(), FileMode.Open, FileAccess.Read, FileShare.Read);
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                EnsureCurrent();
                if (_current == null) return 0;
                int read = _current.Read(buffer, offset, count);
                if (read == 0)
                {
                    EnsureCurrent();
                    return Read(buffer, offset, count);
                }
                return read;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                EnsureCurrent();
                if (_current == null) return 0;
                int read = await _current.ReadAsync(buffer.AsMemory(offset, count), ct);
                if (read == 0)
                {
                    EnsureCurrent();
                    return await ReadAsync(buffer, offset, count, ct);
                }
                return read;
            }

            protected override void Dispose(bool disposing)
            {
                _current?.Dispose(); base.Dispose(disposing);
            }
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
