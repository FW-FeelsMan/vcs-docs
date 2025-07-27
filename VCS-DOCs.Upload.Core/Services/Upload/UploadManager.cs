using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core.Models;
using VCS_DOCs.Infrastructure.Services.Storage;
using System.IO;

namespace VCS_DOCs.Upload.Core;

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

    public async Task<(long usedBytes, long tempBytes, long limitBytes)> GetStorageStatsAsync(string shortUserId)
    {
        var used = await _storage.GetUsedBytesAsync(shortUserId);
        var temp = await _storage.GetTempBytesAsync(shortUserId);
        var limit = await _userInfoProvider.GetUserStorageLimitAsync(shortUserId);
        return (used, temp, limit);
    }
    public async Task<ChunkUploadResponse> HandleChunkUploadAsync(
     string userId,
     IFormFile chunk,
     string hash,
     int chunkIndex,
     int totalChunks,
     long fileSize,
     int? replaceVersion,
     string fileName,
     Guid? sessionId = null)
    {
        if (chunk == null || chunk.Length == 0)
            return new ChunkUploadResponse { Message = "Чанк пустой или отсутствует" };

        var shortUserId = UserIdHelper.ToShortId(userId);
        FileUploadSessionModel? session = null;

        if (sessionId.HasValue)
        {
            session = await _db.FileUploadSessions
                .FirstOrDefaultAsync(s =>
                    s.FileId == sessionId.Value &&
                    s.UserId == userId &&
                    s.Status == "uploading");
        }

        if (session == null)
        {
            session = await _db.FileUploadSessions
                .FirstOrDefaultAsync(s =>
                    s.UserId == userId &&
                    s.FileHash == hash &&
                    s.Status == "uploading");
        }

        if (session == null && replaceVersion.HasValue)
        {
            session = await _db.FileUploadSessions
                .FirstOrDefaultAsync(s =>
                    s.UserId == userId &&
                    s.OriginalFileName == fileName &&
                    s.Version == replaceVersion.Value &&
                    s.Status != "deleted");

            if (session != null)
            {
                session.Status = "uploading";
                session.FileHash = hash;
                session.FileSize = fileSize;
                session.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        if (session == null)
        {
            var latestComplete = await _db.FileUploadSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.OriginalFileName == fileName && s.Status == "complete")
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();

            var nextVersion = (latestComplete?.Version ?? 0) + 1;
            var fileGroupId = latestComplete?.FileGroupId ?? Guid.NewGuid();

            session = new FileUploadSessionModel
            {
                FileId = Guid.NewGuid(),
                FileGroupId = fileGroupId,
                UserId = userId,
                OriginalFileName = fileName,
                FileHash = hash,
                FileSize = fileSize,
                Status = "uploading",
                UpdatedAt = DateTime.UtcNow,
                Version = nextVersion
            };

            await _db.FileUploadSessions.AddAsync(session);
            await _db.SaveChangesAsync();
        }

        // 💾 Сохраняем чанк
        var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
        Directory.CreateDirectory(chunkDir);
        Console.WriteLine($"[DEBUG] Используется FileId: {session.FileId}");

        var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex}");

        using (var stream = new FileStream(chunkPath, FileMode.Create))
        {
            await chunk.CopyToAsync(stream);
        }

        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new ChunkUploadResponse
        {
            SessionId = session.FileId,
            Message = "Чанк принят"
        };
    }

    public async Task<(bool Success, string Message)> CompleteSessionAsync(string userId, string hash)
    {
        var session = await _db.FileUploadSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.FileHash == hash && s.Status == "uploading");

        if (session == null)
            return (false, "Сессия не найдена");

        var shortUserId = UserIdHelper.ToShortId(userId);
        var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);

        if (!Directory.Exists(chunkDir))
            return (false, "Чанки не найдены");

        var orderedChunks = Directory.GetFiles(chunkDir, "chunk_*")
            .OrderBy(f => int.Parse(Path.GetFileName(f).Replace("chunk_", "")))
            .ToList();

        var fileStorageDir = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), session.Version);
        Directory.CreateDirectory(fileStorageDir);

        var finalFilePath = _paths.GetFilePath(shortUserId, session.FileGroupId.ToString(), session.Version, session.OriginalFileName);

        using (var finalStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var chunk in orderedChunks)
            {
                var length = new FileInfo(chunk).Length;
                Console.WriteLine($"[DEBUG] Обрабатываем чанк {chunk}, размер: {length} байт");

                using (var chunkStream = new FileStream(chunk, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await chunkStream.CopyToAsync(finalStream);
                    Console.WriteLine($"[DEBUG] Копирован чанк {chunk}.");
                }
            }

            await finalStream.FlushAsync();
            Console.WriteLine($"[DEBUG] Файл собран: {finalFilePath}, размер: {new FileInfo(finalFilePath).Length} байт");
        }

        session.Status = "complete";
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            if (Directory.Exists(chunkDir))
            {
                Directory.Delete(chunkDir, true);
                Console.WriteLine($"[CLEANUP] Удалена временная папка чанков: {chunkDir}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLEANUP ERROR] Не удалось удалить папку чанков: {chunkDir}. Ошибка: {ex.Message}");
        }
       
        return (true, "Файл успешно сохранен");
    }

    public async Task<List<UserFileDto>> GetAllUserFilesAsync(string userId)
    {
        var sessions = await _db.FileUploadSessions
            .Where(s => s.UserId == userId && s.Status == "complete")
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

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


    public class FileVersionInfo
    {
        public int Version
        {
            get; set;
        }
        public DateTime UpdatedAt
        {
            get; set;
        }
    }

    public async Task<List<FileVersionInfo>> GetAllVersionsAsync(string userId, string fileName)
    {
        return await _db.FileUploadSessions
            .Where(s => s.UserId == userId && s.OriginalFileName == fileName && s.Status != "deleted")
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new FileVersionInfo
            {
                Version = s.Version,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<(Guid SessionId, List<int> UploadedChunks)?> GetOngoingSessionsByHashAsync(string userId, string hash)
    {
        var session = await _db.FileUploadSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.FileHash == hash && s.Status == "uploading");

        if (session == null)
            return null;

        var shortUserId = UserIdHelper.ToShortId(userId);
        // var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId);
        // var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId, session.Version);
        var chunkDir = _pathValidator.GetChunkDirectory(shortUserId, session.FileId, session.Version);


        if (!Directory.Exists(chunkDir))
            return (session.FileId, new List<int>());

        var uploadedChunks = Directory.GetFiles(chunkDir, "chunk_*")
            .Select(p => int.Parse(Path.GetFileName(p).Replace("chunk_", "")))
            .ToList();

        return (session.FileId, uploadedChunks);
    }

    public async Task<object> CheckConflictAsync(string userId, string fileName, string hash)
    {
        var existing = await _db.FileUploadSessions
            .Where(s => s.UserId == userId && s.OriginalFileName == fileName && s.Status != "deleted")
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        if (existing == null)
            return new { status = "ok" };

        if (existing.Status == "uploading")
            return new { status = "uploading" };

        return new { status = "conflict" };
    }

    public async Task<FileContentResultModel?> GetFileVersionAsync(string userId, Guid fileGroupId, int version)
    {
        var session = await _db.FileUploadSessions
            .FirstOrDefaultAsync(s =>
                s.UserId == userId &&
                s.FileGroupId == fileGroupId &&
                s.Version == version &&
                s.Status == "complete");

        if (session == null)
            return null;

        var shortUserId = UserIdHelper.ToShortId(userId);
        var content = await _storage.ReadFileAsync(shortUserId, session.FileGroupId, session.Version, session.OriginalFileName);

        return new FileContentResultModel
        {
            Content = content,
            FileName = session.OriginalFileName
        };
    }

    public async Task<bool> DeleteFileVersionAsync(string userId, Guid fileGroupId, int version)
    {
        var session = await _db.FileUploadSessions
            .FirstOrDefaultAsync(s =>
                s.UserId == userId &&
                s.FileGroupId == fileGroupId &&
                s.Version == version &&
                s.Status == "complete");

        if (session == null)
        {
            Console.WriteLine($"Попытка удалить несуществующий файл: {fileGroupId}, v{version}");
            return true;
        }

        var shortUserId = UserIdHelper.ToShortId(userId);
        _db.FileUploadSessions.Remove(session);
        await _db.SaveChangesAsync();

        await _storage.DeleteFileAsync(shortUserId, session.FileGroupId, session.Version, session.OriginalFileName);

        var versionDir = _paths.GetVersionedFileFolder(shortUserId, session.FileGroupId.ToString(), version);
        var fileGroupDir = Path.Combine(_paths.GetFileRoot(shortUserId), session.FileGroupId.ToString());

        try
        {
            var metaPath = Path.Combine(versionDir, "meta.json");
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
                Console.WriteLine("Удалён meta.json для {0}, v{1}", fileGroupId, version);
            }

            if (Directory.Exists(versionDir) && Directory.GetFiles(versionDir).Length == 0)
            {
                Directory.Delete(versionDir);
                Console.WriteLine("Удалена пустая папка версии: {0}", versionDir);
            }

            // Проверяем, пуста ли папка FileGroupId
            if (Directory.Exists(fileGroupDir) && Directory.GetDirectories(fileGroupDir).Length == 0)
            {
                Directory.Delete(fileGroupDir);
                Console.WriteLine("Удалена пустая папка файла: {0}", fileGroupDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Не удалось полностью очистить папку версии или файла: {ex}, {versionDir}");
        }

        return true;
    }


    private static class UserIdHelper
    {
        public static string ToShortId(string fullGuid)
            => fullGuid.Replace("-", "").Substring(0, 8);
    }
}
