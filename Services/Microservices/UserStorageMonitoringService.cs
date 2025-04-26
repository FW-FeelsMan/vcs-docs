using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Data.Hubs;

namespace VCS_DOCs.Services.Microservices
{
	public class UserStorageMonitoringService : IUserMicroservice
	{
		private readonly string _userFolderPath;
		private readonly IHubContext<UserStorageHub> _hubContext;
		private FileSystemWatcher _watcher;
		private CancellationTokenSource _cts;

		public string UserId { get; }

		public bool ShouldKeepRunningAfterUserDisconnect => false;

		private const long MaxFolderSizeBytes = 10L * 1024 * 1024 * 1024;

		public UserStorageMonitoringService(string userId, string userFolderPath, IHubContext<UserStorageHub> hubContext)
		{
			UserId = userId;
			_userFolderPath = userFolderPath;
			_hubContext = hubContext;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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

			UpdateFileList();

			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_watcher?.Dispose();
			_cts?.Cancel();
			return Task.CompletedTask;
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			UpdateFileList();
		}

		private void OnRenamed(object sender, RenamedEventArgs e)
		{
			UpdateFileList();
		}

		private void UpdateFileList()
		{
			try
			{
				if (!Directory.Exists(_userFolderPath)) return;

				string[] files = Directory.GetFiles(_userFolderPath);
				var fileInfos = new List<object>();
				long totalSizeBytes = 0;

				foreach (string file in files)
				{
					var fileInfo = new FileInfo(file);
					totalSizeBytes += fileInfo.Length;
					fileInfos.Add(new
					{
						fileInfo.Name,
						SizeMb = Math.Round((double)fileInfo.Length / (1024 * 1024), 2),
						LastWriteTime = fileInfo.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
					});
				}

				_hubContext.Clients.Group(UserId).SendAsync("ReceiveStorageUpdate", fileInfos);

				if (totalSizeBytes > MaxFolderSizeBytes)
				{
					double totalSizeGb = totalSizeBytes / (1024.0 * 1024 * 1024);
					_hubContext.Clients.Group(UserId).SendAsync("ReceiveStorageWarning", $"Превышен лимит хранилища (10 ГБ). Текущий размер: {totalSizeGb:F2} ГБ.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
		}
	}
}
