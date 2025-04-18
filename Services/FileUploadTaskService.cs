using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Hubs;

namespace VCS_DOCs.Services
{
	public class FileUploadTaskService : BackgroundService
	{
		private readonly IHubContext<UserStorageHub> _hubContext;
		private readonly ILogger<FileUploadTaskService> _logger;
		private readonly ConcurrentQueue<FileUploadTask> _tasks = new();
		private readonly UserStorageQuotaService _quotaService;
		private readonly ConcurrentDictionary<string, FileUploadTask> _activeTasks = new();
		private readonly ConcurrentBag<string> _cancelledTaskIds = [];

		public FileUploadTaskService(
			IHubContext<UserStorageHub> hubContext,
			ILogger<FileUploadTaskService> logger,
			UserStorageQuotaService quotaService)
		{
			_hubContext = hubContext;
			_logger = logger;
			_quotaService = quotaService;
		}

		public void EnqueueTask(FileUploadTask task)
		{
			_tasks.Enqueue(task);
			_activeTasks.TryAdd(task.TaskId, task);
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
			string[] chunkFiles = Directory.GetFiles(task.TempFilePath).OrderBy(f => f).ToArray();
			string destinationFile = Path.Combine(task.DestinationFolder, task.OriginalFileName);

			if (_cancelledTaskIds.Contains(task.TaskId))
			{
				if (Directory.Exists(task.TempFilePath))
					Directory.Delete(task.TempFilePath, true);

				_quotaService.ReleaseReservation(task.UserId, task.FileLength);
				RemoveActiveTask(task.TempFilePath);
				_logger.LogInformation($"Загрузка отменена: {task.OriginalFileName}");
				return;
			}

			using (var destinationStream = new FileStream(destinationFile, FileMode.Create))
			{
				foreach (var chunkFile in chunkFiles)
				{
					using (var sourceStream = new FileStream(chunkFile, FileMode.Open))
						await sourceStream.CopyToAsync(destinationStream, stoppingToken);

					File.Delete(chunkFile);
				}
			}

			Directory.Delete(task.TempFilePath);
			_quotaService.ReleaseReservation(task.UserId, task.FileLength);

			await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveUploadProgress", new { fileName = task.OriginalFileName, progress = 100 });

			var fileInfos = Directory.GetFiles(task.DestinationFolder).Select(file => new
			{
				name = Path.GetFileName(file),
				sizeMb = Math.Round(new FileInfo(file).Length / 1048576.0, 2),
				lastWriteTime = File.GetLastWriteTime(file).ToString("dd.MM.yyyy, HH:mm")
			});

			await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveStorageUpdate", fileInfos);

			RemoveActiveTask(task.TempFilePath);
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
			var taskToRemove = _activeTasks.Values.FirstOrDefault(t => t.TempFilePath == tempFilePath);
			if (taskToRemove != null)
				_activeTasks.TryRemove(taskToRemove.TempFilePath, out _);
		}
				public bool IsTaskActiveForFolder(string folderPath)
		{
			if (_activeTasks.ContainsKey(folderPath))
			{
				var lastChunkTime = Directory.GetFiles(folderPath)
					.Select(f => File.GetLastWriteTimeUtc(f))
					.OrderByDescending(t => t)
					.FirstOrDefault();

				if (lastChunkTime == default) return false;

				return (DateTime.UtcNow - lastChunkTime) < TimeSpan.FromSeconds(15);
			}
			return false;
		}
	}
}