using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Data.Hubs;

namespace VCS_DOCs.Services.Upload
{
	public class FileUploadTaskService : BackgroundService
	{
		private readonly IHubContext<UserStorageHub> _hubContext;
		private readonly ILogger<FileUploadTaskService> _logger;
		private readonly ConcurrentQueue<FileUploadTask> _tasks = new();
		private readonly ConcurrentDictionary<string, FileUploadTask> _activeTasks = new();
		private readonly ConcurrentBag<string> _cancelledTaskIds = [];


		public FileUploadTaskService(
			IHubContext<UserStorageHub> hubContext,
			ILogger<FileUploadTaskService> logger)
		{
			_hubContext = hubContext;
			_logger = logger;
		}

		public void EnqueueTask(FileUploadTask task)
		{
			_tasks.Enqueue(task);
			_activeTasks.TryAdd(task.TempFilePath, task);
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				if (_tasks.TryDequeue(out var task))
				{
					await ProcessTaskAsync(task, stoppingToken);
				}
				else
				{
					await Task.Delay(1000, stoppingToken);
				}
			}
		}

		private async Task ProcessTaskAsync(FileUploadTask task, CancellationToken stoppingToken)
		{
			// Получаем все чанки и сортируем их по индексу
			string[] chunkFiles = Directory.GetFiles(task.TempFilePath).OrderBy(f =>
				int.Parse(Path.GetFileName(f).Replace("chunk_", ""))).ToArray();
			string destinationFile = Path.Combine(task.DestinationFolder, task.OriginalFileName);

			if (_cancelledTaskIds.Contains(task.TaskId))
			{
				if (Directory.Exists(task.TempFilePath))
					Directory.Delete(task.TempFilePath, true);

				RemoveActiveTask(task.TempFilePath);
				_logger.LogInformation($"Загрузка отменена: {task.OriginalFileName}");
				return;
			}

			try
			{
				// Определяем оптимальный размер буфера в зависимости от размера файла
				int bufferSize = task.FileLength > 1024 * 1024 * 1024
					? 16 * 1024 * 1024  // 16 МБ буфер для файлов больше 1 ГБ
					: 4 * 1024 * 1024;  // 4 МБ буфер для файлов меньше 1 ГБ

				// Используем оптимизированные настройки FileStream для больших файлов
				using (var destinationStream = new FileStream(
					destinationFile,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					bufferSize,
					FileOptions.Asynchronous | FileOptions.SequentialScan))
				{
					long totalProcessed = 0;
					int chunkCount = chunkFiles.Length;

					for (int i = 0; i < chunkCount; i++)
					{
						var chunkFile = chunkFiles[i];

						using (var sourceStream = new FileStream(
							chunkFile,
							FileMode.Open,
							FileAccess.Read,
							FileShare.ReadWrite,
							bufferSize,
							FileOptions.Asynchronous | FileOptions.SequentialScan))
						{
							await sourceStream.CopyToAsync(destinationStream, bufferSize, stoppingToken);
							totalProcessed += sourceStream.Length;
						}

						// Удаляем чанк после успешного копирования
						TryDeleteFile(chunkFile);

						// Периодически сообщаем о прогрессе (каждые 10% или каждые 50 чанков)
						if (i % Math.Max(1, chunkCount / 10) == 0 || i % 50 == 0)
						{
							double progress = (double)totalProcessed / task.FileLength * 100;
							await _hubContext.Clients.Group(task.UserId).SendAsync(
								"ReceiveUploadProgress",
								new { fileName = task.OriginalFileName, progress = Math.Min(99, progress) });
						}
					}

					// Явно сбрасываем буферы на диск
					await destinationStream.FlushAsync(stoppingToken);
				}

				// Удаляем директорию с чанками после успешной сборки
				TryDeleteDirectory(task.TempFilePath);

				// Сообщаем о завершении загрузки
				await _hubContext.Clients.Group(task.UserId).SendAsync(
					"ReceiveUploadProgress",
					new { fileName = task.OriginalFileName, progress = 100 });

				// Обновляем список файлов
				var fileInfos = Directory.GetFiles(task.DestinationFolder).Select(file => new
				{
					name = Path.GetFileName(file),
					sizeMb = Math.Round(new FileInfo(file).Length / 1048576.0, 2),
					lastWriteTime = File.GetLastWriteTime(file).ToString("dd.MM.yyyy, HH:mm")
				});

				await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveStorageUpdate", fileInfos);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"Ошибка при обработке задачи {task.TaskId}: {ex.Message}");

				// Уведомляем клиента об ошибке
				await _hubContext.Clients.Group(task.UserId).SendAsync(
					"ReceiveUploadError",
					new { fileName = task.OriginalFileName, error = $"Ошибка при сборке файла: {ex.Message}" });
			}
			finally
			{
				RemoveActiveTask(task.TempFilePath);
			}
		}

		private void TryDeleteFile(string filePath)
		{
			try
			{
				if (File.Exists(filePath))
					File.Delete(filePath);
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"Не удалось удалить файл чанка {filePath}: {ex.Message}");
			}
		}

		private void TryDeleteDirectory(string dirPath)
		{
			try
			{
				if (Directory.Exists(dirPath))
					Directory.Delete(dirPath, true);
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"Не удалось удалить каталог чанков {dirPath}: {ex.Message}");
			}
		}

		public bool CancelTask(string taskId)
		{
			_cancelledTaskIds.Add(taskId);
			return true;
		}

		public void RegisterActiveTask(FileUploadTask task)
		{
			_activeTasks.TryAdd(task.TempFilePath, task);
		}

		public void RemoveActiveTask(string tempFilePath)
		{
			_activeTasks.TryRemove(tempFilePath, out _);
		}

		public bool IsTaskActiveForFolder(string folderPath)
		{
			if (_activeTasks.TryGetValue(folderPath, out var task))
			{
				var lastChunkTime = Directory.GetFiles(folderPath)
					.Select(f => File.GetLastWriteTimeUtc(f))
					.OrderByDescending(t => t)
					.FirstOrDefault();

				if (lastChunkTime == default) return false;

				return DateTime.UtcNow - lastChunkTime < TimeSpan.FromSeconds(15);
			}
			return false;
		}
	}
}
