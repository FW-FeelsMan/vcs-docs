using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace VCS_DOCs.Services
{
	public class UserStorageQuotaService
	{
		public const long MaxUserStorageBytes = 10L * 1024 * 1024 * 1024;

		private readonly IWebHostEnvironment _env;

		private readonly ConcurrentDictionary<string, long> _reservedBytes = new();
		private readonly ConcurrentDictionary<string, string> _usernames = new();

		public UserStorageQuotaService(IWebHostEnvironment env)
		{
			_env = env;
		}

		public bool TryReserve(string userId, long size, long usedBytes)
		{
			long reserved = _reservedBytes.GetOrAdd(userId, 0);
			if (usedBytes + reserved + size > MaxUserStorageBytes)
				return false;

			long newReserved = reserved + size;
			_reservedBytes[userId] = newReserved;

			if (_usernames.TryGetValue(userId, out var username))
			{
				Console.WriteLine($"[QuotaService] Сохраняем резерв {newReserved} для {username}");
				SaveReservedBytesToFile(userId, username, newReserved);
			}
			else
			{
				Console.WriteLine($"[QuotaService] ОШИБКА: username не найден для userId={userId}");
			}

			return true;
		}

		public void ReleaseReservation(string userId, long size)
		{
			if (!_usernames.TryGetValue(userId, out var username))
			{
				Console.WriteLine($"[QuotaService] Username для {userId} не найден, квота не сохранена");
				return;
			}

			if (_reservedBytes.TryGetValue(userId, out var reserved))
			{
				long updated = Math.Max(0, reserved - size);
				_reservedBytes[userId] = updated;

				Console.WriteLine($"[QuotaService] Освобождаем резерв, сохраняем {updated} для {username}");
				SaveReservedBytesToFile(userId, username, updated);
			}
		}
		public void ForceSetReservation(string userId, string username, long bytes)
		{
			_reservedBytes[userId] = bytes;
			_usernames[userId] = username;
			SaveReservedBytesToFile(userId, username, bytes);
		}

		public long GetReservedBytes(string userId)
		{
			return _reservedBytes.TryGetValue(userId, out var value) ? value : 0;
		}

		public void RegisterUser(string userId, string username)
		{
			_usernames[userId] = username;

			long restored = LoadReservedBytesFromFile(username);
			_reservedBytes[userId] = restored;

			Console.WriteLine($"[QuotaService] Пользователь {username} зарегистрирован с резервом {restored} байт.");

			SaveReservedBytesToFile(userId, username, restored);
		}

		private void SaveReservedBytesToFile(string userId, string username, long reservedBytes)
		{
			string userPath = Path.Combine(_env.ContentRootPath, "Data", "userData", $"userData_{username}");
			string iniFile = Path.Combine(userPath, $"history_{username}.ini");

			var lines = File.Exists(iniFile) ? File.ReadAllLines(iniFile).ToList() : new List<string>();

			bool quotaWritten = false;
			int quotaIndex = lines.IndexOf("[Quota]");

			if (quotaIndex >= 0)
			{
				// Ищем ReservedBytes и обновляем
				for (int i = quotaIndex + 1; i < lines.Count && !lines[i].StartsWith("["); i++)
				{
					if (lines[i].StartsWith("ReservedBytes="))
					{
						lines[i] = $"ReservedBytes={reservedBytes}";
						quotaWritten = true;
						break;
					}
				}

				if (!quotaWritten)
					lines.Insert(quotaIndex + 1, $"ReservedBytes={reservedBytes}");
			}
			else
			{
				lines.Add("[Quota]");
				lines.Add($"ReservedBytes={reservedBytes}");
			}

			File.WriteAllLines(iniFile, lines);
		}

		private long LoadReservedBytesFromFile(string username)
		{
			string userPath = Path.Combine(_env.ContentRootPath, "Data", "userData", $"userData_{username}");
			string iniFile = Path.Combine(userPath, $"history_{username}.ini");

			if (!File.Exists(iniFile))
				return 0;

			string[] lines = File.ReadAllLines(iniFile);
			bool inQuotaSection = false;

			foreach (var line in lines)
			{
				if (line.Trim() == "[Quota]")
				{
					inQuotaSection = true;
					continue;
				}
				if (inQuotaSection && line.StartsWith("ReservedBytes="))
				{
					string valueStr = line.Split('=').Last();
					if (long.TryParse(valueStr, out var value))
						return value;
				}
			}
			return 0;
		}
	}
}