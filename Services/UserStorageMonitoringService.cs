using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Hubs;

namespace VCS_DOCs.Services
{
	public class UserStorageMonitoringService : BackgroundService
	{
		private readonly string _userId;
		private readonly string _userFolderPath;
		private readonly ILogger<UserStorageMonitoringService> _logger;
		private readonly IHubContext<UserStorageHub> _hubContext;
		private FileSystemWatcher _watcher;

		public UserStorageMonitoringService(string userId, string userFolderPath, ILogger<UserStorageMonitoringService> logger, IHubContext<UserStorageHub> hubContext)
		{
			_userId = userId;
			_userFolderPath = userFolderPath;
			_logger = logger;
			_hubContext = hubContext;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation($"Запущен мониторинг хранилища пользователя {_userId} в папке {_userFolderPath}.");

			// Настройка наблюдения за папкой
			_watcher = new FileSystemWatcher(_userFolderPath)
			{
				IncludeSubdirectories = false,
				EnableRaisingEvents = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
			};

			_watcher.Created += OnChanged;
			_watcher.Deleted += OnChanged;
			_watcher.Changed += OnChanged;
			_watcher.Renamed += OnRenamed;

			// Первоначальное обновление списка файлов
			UpdateFileList();

			return Task.CompletedTask;
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			_logger.LogInformation($"Файл изменён: {e.FullPath} ({e.ChangeType})");
			UpdateFileList();
		}

		private void OnRenamed(object sender, RenamedEventArgs e)
		{
			_logger.LogInformation($"Файл переименован: {e.OldFullPath} -> {e.FullPath}");
			UpdateFileList();
		}

		private void UpdateFileList()
		{
			try
			{
				string[] files = Directory.GetFiles(_userFolderPath);
				var fileInfos = new List<object>();

				foreach (string file in files)
				{
					FileInfo fileInfo = new FileInfo(file);
					fileInfos.Add(new
					{
						Name = fileInfo.Name,
						SizeMb = Math.Round((double)fileInfo.Length / (1024 * 1024), 2),
						LastWriteTime = fileInfo.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
					});
				}

				// Отправляем обновление на клиентскую сторону в группу данного пользователя
				_hubContext.Clients.Group(_userId).SendAsync("ReceiveStorageUpdate", fileInfos);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка при обновлении списка файлов.");
			}
		}

		public override Task StopAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation($"Остановка мониторинга хранилища пользователя {_userId}.");
			_watcher.Dispose();
			return base.StopAsync(cancellationToken);
		}
	}
}
