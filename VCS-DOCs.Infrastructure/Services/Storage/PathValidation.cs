using Microsoft.Extensions.Options;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VCS_DOCs.Configuration;

namespace VCS_DOCs.Infrastructure.Services.Storage
{
	public class FilePathValidator
	{
		private readonly string _baseStoragePath;
		private const int MaxPathLength = 260;

		private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

		public FilePathValidator(IOptions<UserDataPathOptions> options)
		{
			_baseStoragePath = options.Value.BasePath;
		}

		private string NormalizeUserId(string userId)
		{
			// Используем только первую часть userId (до первого '-')
			return userId.Split('-')[0];
		}

		public bool IsSafeToStore(string userId, string fileNameOrHash, out string safePath, out string error)
		{
			fileNameOrHash = Path.GetFileName(fileNameOrHash);
			fileNameOrHash = new string(fileNameOrHash.Where(c => !InvalidFileNameChars.Contains(c)).ToArray());

			var shortUserId = NormalizeUserId(userId);
			var relativePath = Path.Combine($"u_{shortUserId}", fileNameOrHash);
			safePath = Path.Combine(_baseStoragePath, relativePath);
			var fullPath = Path.GetFullPath(safePath);

			if (!fullPath.StartsWith(Path.GetFullPath(_baseStoragePath)))
			{
				error = "Попытка выхода за пределы допустимого пути";
				return false;
			}

			if (fullPath.Length > MaxPathLength)
			{
				error = $"Путь слишком длинный: {fullPath.Length} символов";
				return false;
			}

			error = string.Empty;
			return true;
		}

        public string GetChunkDirectory(string userId, Guid sessionId, int? version = null)
        {
            var shortUserId = NormalizeUserId(userId);
            var path = version == null
                ? Path.Combine(_baseStoragePath, $"u_{shortUserId}", "chunks", sessionId.ToString())
                : Path.Combine(_baseStoragePath, $"u_{shortUserId}", "chunks", sessionId.ToString(), $"v{version}");

            return Path.GetFullPath(path);
        }
        public string GetChunkPath(string userId, Guid sessionId, int chunkIndex, int? version = null)
        {
            var chunkDir = GetChunkDirectory(userId, sessionId, version);
            return Path.Combine(chunkDir, $"chunk_{chunkIndex}");
        }
    }
}
