using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
			string destinationFile = Path.Combine(task.DestinationFolder, task.OriginalFileName);
			using (var sourceStream = File.OpenRead(task.TempFilePath))
			using (var destinationStream = new FileStream(destinationFile, FileMode.Create))
			{
				byte[] buffer = new byte[81920];
				long totalRead = 0;
				int read;
				while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, stoppingToken)) > 0)
				{
					await destinationStream.WriteAsync(buffer, 0, read, stoppingToken);
					totalRead += read;
					double progress = (double)totalRead / task.FileLength * 100;
					await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveUploadProgress", new { fileName = task.OriginalFileName, progress = progress });
				}
			}
			await _hubContext.Clients.Group(task.UserId).SendAsync("ReceiveUploadProgress", new { fileName = task.OriginalFileName, progress = 100 });
			File.Delete(task.TempFilePath);
		}
	}
}
