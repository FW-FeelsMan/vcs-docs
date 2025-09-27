// D:\Unity\VCS-DOCs\VCS-DOCs.Infrastructure\Services\Storage\PhysicalFileStorageService.cs
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

            // Пишем асинхронно, крупным буфером.
            await using (var fs = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    totalBytes += read;
                }
                await fs.FlushAsync();
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

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Console.WriteLine($"Удалён файл: {filePath}");
                }
                else
                {
                    Console.WriteLine($"Файл не найден и не был удалён: {filePath}");
                }
            }
            catch { /* ignore */ }

            try
            {
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                    Console.WriteLine($"Удалён meta.json: {metaPath}");
                }
            }
            catch { /* ignore */ }

            try
            {
                if (Directory.Exists(versionDir) &&
                    Directory.GetFiles(versionDir).Length == 0 &&
                    Directory.GetDirectories(versionDir).Length == 0)
                {
                    Directory.Delete(versionDir);
                    Console.WriteLine($"Удалена пустая директория версии: {versionDir}");
                }
            }
            catch { /* ignore */ }

            return Task.CompletedTask;
        }

        public Task<long> GetUsedBytesAsync(string shortUserId)
        {
            var userDir = Path.Combine(_basePath, $"u_{shortUserId}", "files");
            if (!Directory.Exists(userDir)) return Task.FromResult(0L);

            long total = 0;
            try
            {
                // Быстрое перечисление; исключаем гонки на исчезающих файлах
                foreach (var f in Directory.EnumerateFiles(userDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Exists) total += fi.Length;
                    }
                    catch { /* пропускаем файл, который успели удалить/переместить */ }
                }
            }
            catch { /* ignore root errors */ }

            return Task.FromResult(total);
        }

        public Task<long> GetTempBytesAsync(string shortUserId)
        {
            var tempDir = Path.Combine(_basePath, $"u_{shortUserId}", "chunks");
            if (!Directory.Exists(tempDir)) return Task.FromResult(0L);

            long total = 0;
            try
            {
                // Раньше тут был .Sum(new FileInfo(f).Length) — падало, если файл исчез.
                // Теперь безопасно суммируем с проверками.
                foreach (var f in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Exists) total += fi.Length;
                    }
                    catch { /* файл мог исчезнуть между перечислением и чтением длины */ }
                }
            }
            catch { /* ignore root errors */ }

            return Task.FromResult(total);
        }
    }
}
