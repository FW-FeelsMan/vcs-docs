using Microsoft.Extensions.Options;
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

		public async Task SaveFileAsync(string fileHash, Stream content)
		{
			string filePath = Path.Combine(_basePath, fileHash);
			Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
			using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			await content.CopyToAsync(fs);
		}

		public async Task<byte[]> ReadFileAsync(string fileHash)
		{
			string filePath = Path.Combine(_basePath, fileHash);
			if (!File.Exists(filePath))
				throw new FileNotFoundException("Файл не найден", fileHash);

			return await File.ReadAllBytesAsync(filePath);
		}

		public Task DeleteFileAsync(string fileHash)
		{
			string filePath = Path.Combine(_basePath, fileHash);
			if (File.Exists(filePath))
				File.Delete(filePath);
			return Task.CompletedTask;
		}
	}
}
