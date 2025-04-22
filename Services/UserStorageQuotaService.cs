using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace VCS_DOCs.Services
{
	public class UserStorageQuotaService
	{
		public const long MaxUserStorageBytes = 10L * 1024 * 1024 * 1024;

		private readonly IWebHostEnvironment _env;
		private readonly Dictionary<string, string> _usernames = new();

		public UserStorageQuotaService(IWebHostEnvironment env)
		{
			_env = env;
		}

		public bool TryReserve(string userId, string fileName, long fileSize, long usedBytes)
		{
			if (!_usernames.TryGetValue(userId, out var username))
			{
				Console.WriteLine($"[QuotaService] Не найден username для {userId}");
				return false;
			}

			long reserved = GetTotalReservedBytes(username);
			if (usedBytes + reserved + fileSize > MaxUserStorageBytes)
				return false;

			string userPath = Path.Combine(_env.ContentRootPath, "Data", "userData", $"userData_{username}");
			string iniFile = Path.Combine(userPath, $"history_{username}.ini");

			var lines = File.Exists(iniFile) ? File.ReadAllLines(iniFile).ToList() : new List<string>();
			int quotaIndex = lines.IndexOf("[Quota]");
			if (quotaIndex == -1)
			{
				lines.Add("[Quota]");
				lines.Add($"{fileName}={fileSize}");
			}
			else
			{
				bool updated = false;
				for (int i = quotaIndex + 1; i < lines.Count && !lines[i].StartsWith("["); i++)
				{
					if (lines[i].StartsWith($"{fileName}=", StringComparison.OrdinalIgnoreCase))
					{
						lines[i] = $"{fileName}={fileSize}";
						updated = true;
						break;
					}
				}
				if (!updated)
					lines.Insert(quotaIndex + 1, $"{fileName}={fileSize}");
			}

			File.WriteAllLines(iniFile, lines);
			return true;
		}

		public void ReleaseFileReservation(string userId, string fileName)
		{
			if (!_usernames.TryGetValue(userId, out var username))
				return;

			string userPath = Path.Combine(_env.ContentRootPath, "Data", "userData", $"userData_{username}");
			string iniFile = Path.Combine(userPath, $"history_{username}.ini");

			if (!File.Exists(iniFile))
				return;

			var lines = File.ReadAllLines(iniFile).ToList();
			int quotaIndex = lines.IndexOf("[Quota]");
			if (quotaIndex == -1)
				return;

			for (int i = quotaIndex + 1; i < lines.Count && !lines[i].StartsWith("["); i++)
			{
				if (lines[i].StartsWith($"{fileName}=", StringComparison.OrdinalIgnoreCase))
				{
					lines.RemoveAt(i);
					break;
				}
			}

			File.WriteAllLines(iniFile, lines);
		}

		public long GetTotalReservedBytes(string username)
		{
			string userPath = Path.Combine(_env.ContentRootPath, "Data", "userData", $"userData_{username}");
			string iniFile = Path.Combine(userPath, $"history_{username}.ini");

			if (!File.Exists(iniFile))
				return 0;

			string[] lines = File.ReadAllLines(iniFile);
			bool inQuotaSection = false;
			long total = 0;

			foreach (var line in lines)
			{
				if (line.Trim() == "[Quota]")
				{
					inQuotaSection = true;
					continue;
				}
				if (inQuotaSection)
				{
					if (line.StartsWith("["))
						break;

					var parts = line.Split('=');
					if (parts.Length == 2 && long.TryParse(parts[1], out var value))
					{
						total += value;
					}
				}
			}

			return total;
		}

		public void RegisterUser(string userId, string username)
		{
			_usernames[userId] = username;
		}
	}
}
