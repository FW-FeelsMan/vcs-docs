using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public class UserChunkCleanerService
	{
		private readonly string _userId;
		private readonly string _userDataPath;
		private readonly FileUploadTaskService _uploadTaskService;
		private readonly UserStorageQuotaService _quotaService;
		private readonly ILogger<UserChunkCleanerService> _logger;

		public UserChunkCleanerService(
			string userId,
			string userDataPath,
			FileUploadTaskService uploadTaskService,
			UserStorageQuotaService quotaService,
			ILogger<UserChunkCleanerService> logger)
		{
			_userId = userId;
			_userDataPath = userDataPath;
			_uploadTaskService = uploadTaskService;
			_quotaService = quotaService;
			_logger = logger;
		}

		public async Task RunAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					if (!Directory.Exists(_userDataPath))
					{
					await Task.Delay(10000, stoppingToken);
					continue;
					}
					_logger.LogInformation($"[UserCleaner:{_userId}] Папка существует или принудительно продолжаем");
					var chunkDirs = Directory.GetDirectories(_userDataPath, "*_chunks", SearchOption.TopDirectoryOnly);
					_logger.LogInformation($"[UserCleaner:{_userId}] Найдено папок чанков: {chunkDirs.Length}");


					_logger.LogInformation($"[UserCleaner:{_userId}] Сканирование папки {_userDataPath}");

					foreach (var chunkDir in chunkDirs)
					{
						_logger.LogInformation($"[UserCleaner:{_userId}] Найден каталог чанков: {chunkDir}");
						bool isActive = _uploadTaskService.IsTaskActiveForFolder(chunkDir);
						_logger.LogInformation($"[UserCleaner:{_userId}] Задача активна: {isActive}");

						if (!isActive)
						{
							long chunkSize = Directory.GetFiles(chunkDir).Sum(f => new FileInfo(f).Length);
							Directory.Delete(chunkDir, true);
							_quotaService.ReleaseReservation(_userId, chunkSize);
							_uploadTaskService.RemoveActiveTask(chunkDir);
							_logger.LogInformation($"[UserCleaner:{_userId}] Очистка: {chunkDir}, освобождено {chunkSize} байт.");
						}
					}

				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $"[UserCleaner:{_userId}] Ошибка при очистке чанков");
				}

				await Task.Delay(10000, stoppingToken);
			}
		}
	}
}