using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Services.Microservices;

namespace VCS_DOCs.Services.Upload
{
	public class UserChunkCleanerService : IUserMicroservice
	{
		private readonly string _userDataPath;
		private readonly FileUploadTaskService _uploadTaskService;
		private readonly ApplicationDbContext _dbContext;

		public string UserId { get; }
		public bool ShouldKeepRunningAfterUserDisconnect => false;

		private CancellationTokenSource _cts;
		private Task _backgroundTask;

		public UserChunkCleanerService(
			string userId,
			string userDataPath,
			FileUploadTaskService uploadTaskService,
			ApplicationDbContext dbContext)
		{
			UserId = userId;
			_userDataPath = userDataPath;
			_uploadTaskService = uploadTaskService;
			_dbContext = dbContext;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_backgroundTask = Task.Run(() => RunAsync(_cts.Token));
			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_cts.Cancel();
			await RunOneCleanupAsync(force: true);
		}

		private async Task RunAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				await RunOneCleanupAsync();
				await Task.Delay(5000, token);
			}
		}

		private async Task RunOneCleanupAsync(bool force = false)
		{
			if (!Directory.Exists(_userDataPath))
				return;

			var chunkDirs = Directory.GetDirectories(_userDataPath, "*_chunks", SearchOption.TopDirectoryOnly);

			foreach (var chunkDir in chunkDirs)
			{
				string folderName = Path.GetFileName(chunkDir);
				string fileName = folderName.Replace("_chunks", "");

				bool isActive = _uploadTaskService.IsTaskActiveForFolder(chunkDir);

				if (!force && ActiveUploadsRegistry.IsActive(UserId, fileName))
				{
					continue;
				}

				try
				{
					Directory.Delete(chunkDir, recursive: true);
					_uploadTaskService.RemoveActiveTask(chunkDir);					
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Cleaner:{UserId}] Ошибка при удалении {chunkDir}: {ex}");
				}
			}
		}
	}
}