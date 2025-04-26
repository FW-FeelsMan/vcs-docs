using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public interface IStorageQuotaService
	{
		/// <summary>Резервирует <paramref name="bytes"/> байт под файл <paramref name="fileName"/> для пользователя <paramref name="userId"/>.</summary>
		Task<bool> ReserveAsync(string userId, string fileName, long bytes);

		/// <summary>Освобождает ранее зарезервированные <paramref name="bytes"/> байт для файла <paramref name="fileName"/>.</summary>
		Task ReleaseAsync(string userId, string fileName);

		/// <summary>Сколько всего байт сейчас зарезервировано (IsReleased == false).</summary>
		Task<long> GetReservedBytesAsync(string userId);

		/// <summary>Сколько байт занято реально загруженными файлами на диске.</summary>
		Task<long> GetUsedBytesAsync(string userId);

		/// <summary>Очищает висящие незавершённые резервации файлов при старте сервера.</summary>
		Task CleanUpBrokenReservationsAsync(); 
	}
}
