using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;
using VCS_DOCs.Configuration;
using VCS_DOCs.Core.Interfaces;

namespace VCS_DOCs.Infrastructure.Services.Storage
{
    public class PhysicalFileStorageService : IFileStorageService
    {
        private readonly string _basePath;

        public PhysicalFileStorageService(IOptions<UserDataPathOptions> options)
        {
            _basePath = options.Value.BasePath;
        }

        public string GetBasePath() => _basePath;

        public async Task SaveFileAsync(string userIdShort, Guid fileId, int version, string fileName, Stream content)
        {
            var versionedDir = Path.Combine(_basePath, $"u_{userIdShort}", "files", fileId.ToString(), $"v{version}");
            Directory.CreateDirectory(versionedDir);

            var filePath = Path.Combine(versionedDir, fileName);
            long totalBytes = 0;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }
            }

            var meta = new
            {
                FileName = fileName,
                FileId = fileId,
                Version = version,
                Size = totalBytes,
                UploadedAt = DateTime.UtcNow
            };

            var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            var metaPath = Path.Combine(versionedDir, "meta.json");
            await File.WriteAllTextAsync(metaPath, metaJson);
        }

        public async Task<byte[]> ReadFileAsync(string userIdShort, Guid fileId, int version, string fileName)
        {
            var filePath = Path.Combine(_basePath, $"u_{userIdShort}", "files", fileId.ToString(), $"v{version}", fileName);
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл не найден", fileName);

            return await File.ReadAllBytesAsync(filePath);
        }

        public Task DeleteFileAsync(string userIdShort, Guid fileId, int version, string fileName)
        {
            var versionDir = Path.Combine(_basePath, $"u_{userIdShort}", "files", fileId.ToString(), $"v{version}");
            var filePath = Path.Combine(versionDir, fileName);
            var metaPath = Path.Combine(versionDir, "meta.json");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Console.WriteLine($"Удалён файл: {filePath}");
            }
            else
            {
                Console.WriteLine($"Файл не найден и не был удалён: {filePath}");
            }
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
                Console.WriteLine($"Удалён meta.json: {metaPath}");
            }

            if (Directory.Exists(versionDir) && Directory.GetFiles(versionDir).Length == 0)
            {
                Directory.Delete(versionDir);
                Console.WriteLine($"Удалена пустая директория версии: {versionDir}");
            }

            return Task.CompletedTask;
        }

        public Task<long> GetUsedBytesAsync(string shortUserId)
        {
            var userDir = Path.Combine(_basePath, $"u_{shortUserId}", "files");
            if (!Directory.Exists(userDir)) return Task.FromResult(0L);

            long total = Directory.GetFiles(userDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);

            return Task.FromResult(total);
        }

        public Task<long> GetTempBytesAsync(string shortUserId)
        {
            var tempDir = Path.Combine(_basePath, $"u_{shortUserId}", "chunks");
            if (!Directory.Exists(tempDir)) return Task.FromResult(0L);

            long total = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);

            return Task.FromResult(total);
        }
    }
}
