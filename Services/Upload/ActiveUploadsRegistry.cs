using System;
using System.Collections.Concurrent;

namespace VCS_DOCs.Services.Upload
{
	public static class ActiveUploadsRegistry
	{
		private static readonly ConcurrentDictionary<string, DateTime> ActiveUploads = new();

		private static string GetKey(string userId, string fileName)
		{
			return $"{userId}:{fileName.Trim().ToLowerInvariant()}";
		}

		public static void Register(string userId, string fileName)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return;

			ActiveUploads[GetKey(userId, fileName)] = DateTime.UtcNow;
		}

		public static void Touch(string userId, string fileName)
		{
			var key = GetKey(userId, fileName);
			if (ActiveUploads.ContainsKey(key))
				ActiveUploads[key] = DateTime.UtcNow;
		}

		public static bool IsActive(string userId, string fileName)
		{
			var key = GetKey(userId, fileName);
			if (!ActiveUploads.TryGetValue(key, out var lastUpdate))
				return false;

			return (DateTime.UtcNow - lastUpdate) < TimeSpan.FromSeconds(30);
		}

		public static void Unregister(string userId, string fileName)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return;

			ActiveUploads.TryRemove(GetKey(userId, fileName), out _);
		}
	}
}
