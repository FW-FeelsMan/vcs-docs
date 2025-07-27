using System.Security.Cryptography;
using System.Text.Json;

namespace VCS_DOCs.Upload.Core.Services.Tasks
{
	public class ChunkHashService
	{
		private readonly UserStoragePaths _paths;

		public ChunkHashService(UserStoragePaths paths)
		{
			_paths = paths;
		}

		public string ComputeHash(string filePath)
		{
			using var md5 = MD5.Create();
			using var stream = File.OpenRead(filePath);
			var hash = md5.ComputeHash(stream);
			return Convert.ToHexString(hash);
		}

		public void SaveChunkHash(string userIdShort, Guid sessionId, int chunkIndex, string hash)
		{
			string jsonPath = _paths.GetChunkHashJsonTempPath(userIdShort, sessionId);
			Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

			Dictionary<int, string> hashes = [];

			if (File.Exists(jsonPath))
			{
				string existing = File.ReadAllText(jsonPath);
				hashes = JsonSerializer.Deserialize<Dictionary<int, string>>(existing) ?? [];
			}

			hashes[chunkIndex] = hash;

			var json = JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(jsonPath, json);
		}

		public Dictionary<int, string> LoadChunkHashes(string userIdShort, Guid sessionId)
		{
			string jsonPath = _paths.GetChunkHashJsonTempPath(userIdShort, sessionId);
			if (!File.Exists(jsonPath)) return [];

			string json = File.ReadAllText(jsonPath);
			return JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? [];
		}

		public void MoveChunkHashToFinalLocation(string userIdShort, Guid sessionId, string fileHash, int version)
		{
			string src = _paths.GetChunkHashJsonTempPath(userIdShort, sessionId);
			string dst = _paths.GetChunkHashJsonFinalPath(userIdShort, fileHash, version);

			if (!File.Exists(src)) return;

			Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
			File.Move(src, dst, overwrite: true);
		}
	}
}