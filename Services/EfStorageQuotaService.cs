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

		public async Task<bool> ReserveAsync(string userId, string finalFileName, long bytes)
		{
			var used = await GetUsedBytesAsync(userId);
			var reserved = await GetReservedBytesAsync(userId);

			if (used + reserved + bytes > MaxBytes)
				return false;

			var existing = await _db.FileReservations
				.FirstOrDefaultAsync(r => r.UserId == userId && r.FileName == finalFileName);

			if (existing != null)
			{
				existing.ReservedBytes = bytes;
				existing.CreatedAt = DateTime.UtcNow;
			}
			else
			{
				_db.FileReservations.Add(new FileReservation
				{
					UserId = userId,
					FileName = finalFileName,
					ReservedBytes = bytes,
					CreatedAt = DateTime.UtcNow
				});
			}

			await _db.SaveChangesAsync();
			return true;
		}

		public async Task ReleaseAsync(string userId, string fileName)
		{
			var reservations = await _db.FileReservations
				.Where(r => r.UserId == userId && r.FileName == fileName)
				.ToListAsync();

			if (reservations.Any())
			{
				_db.FileReservations.RemoveRange(reservations);
				await _db.SaveChangesAsync();
			}

			Console.WriteLine($"[Release] Removed reservation for {userId} -> {fileName}");
		}

		public async Task<long> GetReservedBytesAsync(string userId)
		{
			var sum = await _db.FileReservations
				.Where(r => r.UserId == userId)
				.SumAsync(r => (long?)r.ReservedBytes);

			return sum ?? 0L;
		}

		public async Task<long> GetUsedBytesAsync(string userId)
		{
			var user = await _db.Users.FindAsync(userId);
			if (user == null)
				return 0;

			string path = Path.Combine(_options.BasePath, $"userData_{user.Id}");

			if (!Directory.Exists(path))
				return 0;

			var files = Directory.GetFiles(path);
			long totalBytes = files.Sum(f => new FileInfo(f).Length);
			return totalBytes;
		}

		public async Task CleanUpBrokenReservationsAsync()
		{
			var all = await _db.FileReservations.ToListAsync();

			foreach (var r in all)
			{
				var user = await _db.Users.FindAsync(r.UserId);
				if (user == null)
				{
					_db.FileReservations.Remove(r);
					continue;
				}

				string userFolder = Path.Combine(_options.BasePath, $"userData_{user.Id}");
				string filePath = Path.Combine(userFolder, r.FileName);

				if (!System.IO.File.Exists(filePath))
				{
					_db.FileReservations.Remove(r);
				}
			}

			await _db.SaveChangesAsync();
		}
	}
}