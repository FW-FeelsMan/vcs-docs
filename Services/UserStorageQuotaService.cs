using System.Collections.Concurrent;

namespace VCS_DOCs.Services
{
	public class UserStorageQuotaService
	{
		private readonly ConcurrentDictionary<string, long> _reservedStorage = new();

		public const long MaxUserStorageBytes = 10L * 1024 * 1024 * 1024;

		public long GetReservedBytes(string userId) =>
			_reservedStorage.TryGetValue(userId, out var reserved) ? reserved : 0;

		public bool TryReserve(string userId, long fileSize, long currentUsed)
		{
			var reserved = GetReservedBytes(userId);
			if (currentUsed + reserved + fileSize > MaxUserStorageBytes)
				return false;

			_reservedStorage.AddOrUpdate(userId, fileSize, (_, prev) => prev + fileSize);
			return true;
		}

		public void ReleaseReservation(string userId, long fileSize)
		{
			_reservedStorage.AddOrUpdate(userId, 0, (_, prev) => Math.Max(0, prev - fileSize));
		}
	}
}
