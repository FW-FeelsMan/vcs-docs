using System.Collections.Concurrent;

namespace VCS_DOCs.Services.Upload
{
	public static class ActiveUploadsRegistry
	{
		private static readonly ConcurrentDictionary<string, byte> ActiveUploads = new();

		public static void Register(string userId, string fileName)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return;

			string key = $"{userId}:{fileName}";
			ActiveUploads.TryAdd(key, 0);
		}

		public static void Unregister(string userId, string fileName)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return;

			string key = $"{userId}:{fileName}";
			ActiveUploads.TryRemove(key, out _);
		}

		public static bool IsActive(string userId, string fileName)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return false;

			string key = $"{userId}:{fileName}";
			return ActiveUploads.ContainsKey(key);
		}
	}
}
