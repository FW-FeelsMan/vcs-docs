using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;

namespace VCS_DOCs.Services
{
	public class EfStorageQuotaService : IStorageQuotaService
	{
		private readonly ApplicationDbContext _db;
		private const long MaxBytes = 10L * 1024 * 1024 * 1024;
		private readonly UserDataPathOptions _options;

		public EfStorageQuotaService(ApplicationDbContext db, IOptions<UserDataPathOptions> options)
		{
			_db = db;
			_options = options.Value;
		}

		public async Task<bool> ReserveAsync(string userId, string fileName, long bytes)
		{
			var used = await GetUsedBytesAsync(userId);
			var reserved = await GetReservedBytesAsync(userId);
			if (used + reserved + bytes > MaxBytes)
				return false;

			var existing = await _db.FileReservations
				.FirstOrDefaultAsync(r => r.UserId == userId && r.FileName == fileName && !r.IsReleased);

			if (existing != null)
			{
				existing.ReservedBytes = bytes;
			}
			else
			{
				_db.FileReservations.Add(new FileReservation
				{
					UserId = userId,
					FileName = fileName,
					ReservedBytes = bytes,
					IsReleased = false,
					CreatedAt = DateTime.UtcNow
				});
			}

			await _db.SaveChangesAsync();
			return true;
		}

		public async Task ReleaseAsync(string userId, string fileName)
		{
			var reservations = await _db.FileReservations
				.Where(r => r.UserId == userId && r.FileName == fileName && !r.IsReleased)
				.ToListAsync();

			foreach (var r in reservations)
			{
				r.IsReleased = true;
			}

			await _db.SaveChangesAsync();
		}

		public async Task<long> GetReservedBytesAsync(string userId)
		{
			var sum = await _db.FileReservations
				.Where(r => r.UserId == userId && !r.IsReleased)
				.SumAsync(r => (long?)r.ReservedBytes);

			return sum ?? 0L;
		}

		public async Task<long> GetUsedBytesAsync(string userId)
		{
			var user = await _db.Users.FindAsync(userId);
			if (user == null)
			{
				Console.WriteLine($"[GetUsedBytesAsync] Пользователь не найден: userId={userId}");
				return 0;
			}

			string path = Path.Combine(_options.BasePath, $"userData_{user.Id}");

			Console.WriteLine($"[GetUsedBytesAsync] Проверяем путь: {path}");

			if (!Directory.Exists(path))
			{
				Console.WriteLine($"[GetUsedBytesAsync] Директория не существует: {path}");
				return 0;
			}

			var files = Directory.GetFiles(path);
			Console.WriteLine($"[GetUsedBytesAsync] Найдено файлов: {files.Length}");

			long totalBytes = files.Sum(f => new FileInfo(f).Length);
			Console.WriteLine($"[GetUsedBytesAsync] Общий объем: {totalBytes} байт");

			return totalBytes;
		}
		public async Task CleanUpBrokenReservationsAsync()
		{
			var allUnreleased = await _db.FileReservations
				.Where(r => !r.IsReleased)
				.ToListAsync();

			foreach (var r in allUnreleased)
			{
				var user = await _db.Users.FindAsync(r.UserId);
				if (user == null)
				{
					r.IsReleased = true;
					continue;
				}

				string userFolder = Path.Combine(_options.BasePath, $"userData_{user.Id}");
				string filePath = Path.Combine(userFolder, r.FileName);

				if (!System.IO.File.Exists(filePath))
				{
					r.IsReleased = true;
					Console.WriteLine($"[Cleanup] Помечаем как освобожденную запись о файле {r.FileName} для пользователя ID={user.Id}");
				}
			}

			await _db.SaveChangesAsync();
		}
	}
}