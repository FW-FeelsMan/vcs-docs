using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Hubs;

namespace VCS_DOCs.Services
{
	public class FileUploadTaskService : BackgroundService
	{
		private readonly IHubContext<UserStorageHub> _hubContext;
		private readonly ILogger<FileUploadTaskService> _logger;
		private readonly ConcurrentQueue<FileUploadTask> _tasks = new ConcurrentQueue<FileUploadTask>();
		public FileUploadTaskService(IHubContext<UserStorageHub> hubContext, ILogger<FileUploadTaskService> logger)
		{
			_hubContext = hubContext;
			_logger = logger;
		}
		public void EnqueueTask(FileUploadTask task)
		{
			_tasks.Enqueue(task);
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

			await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveUploadProgress", new { fileName = task.OriginalFileName, progress = 100 });
			string[] files = Directory.GetFiles(task.DestinationFolder);
			var fileInfos = new List<object>();
			foreach (string file in files)
			{
				var fileInfo = new FileInfo(file);
				fileInfos.Add(new
				{
					name = fileInfo.Name,
					sizeMb = Math.Round((double)fileInfo.Length / (1024 * 1024), 2),
					lastWriteTime = fileInfo.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
				});
			}
			await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveStorageUpdate", fileInfos);
		}
	}
}
