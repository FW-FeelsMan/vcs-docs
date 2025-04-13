using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public class ChunkCleanupService : BackgroundService
	{
		private readonly string _rootDataPath;
		private readonly ILogger<ChunkCleanupService> _logger;
		private readonly FileUploadTaskService _taskService;

		public ChunkCleanupService(IWebHostEnvironment env, ILogger<ChunkCleanupService> logger, FileUploadTaskService taskService)
		{
			_rootDataPath = Path.Combine(env.ContentRootPath, "Data", "userData");
			_logger = logger;
			_taskService = taskService;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					ScanAndCleanupChunks();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Ошибка при очистке .zip_chunks директорий");
				}

				await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
			}
		}

		private void ScanAndCleanupChunks()
		{
			if (!Directory.Exists(_rootDataPath))
				return;

			foreach (var userDir in Directory.GetDirectories(_rootDataPath))
			{
				var chunkDirs = Directory.GetDirectories(userDir, "*_chunks", SearchOption.AllDirectories);

				foreach (var chunkDir in chunkDirs)
				{
					string fileName = Path.GetFileNameWithoutExtension(chunkDir).Replace("_chunks", "");

					bool isTaskActive = _taskService.IsTaskActiveForFolder(chunkDir);
					if (!isTaskActive)
					{
						try
						{
							Directory.Delete(chunkDir, true);
							_logger.LogInformation($"Удалена неиспользуемая директория: {chunkDir}");
						}
						catch (Exception ex)
						{
							_logger.LogWarning(ex, $"Не удалось удалить {chunkDir}");
						}
					}
				}
			}
		}
	}
}
