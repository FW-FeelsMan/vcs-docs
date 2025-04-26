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
			await RunOneCleanupAsync();
		}

		private async Task RunAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				await RunOneCleanupAsync();
				await Task.Delay(5000, token);
			}
		}

		private async Task RunOneCleanupAsync()
		{
			if (!Directory.Exists(_userDataPath))
				return;

			var chunkDirs = Directory.GetDirectories(_userDataPath, "*_chunks", SearchOption.TopDirectoryOnly);

			foreach (var chunkDir in chunkDirs)
			{
				bool isActive = _uploadTaskService.IsTaskActiveForFolder(chunkDir);
				long chunkSize = Directory.GetFiles(chunkDir).Sum(f => new FileInfo(f).Length);
				string folderName = Path.GetFileName(chunkDir);
				string fileName = folderName.Replace("_chunks", ""); // <-- восстанавливаем имя исходного файла без "_chunks"

				// Проверка через ActiveUploadsRegistry
				if (ActiveUploadsRegistry.IsActive(UserId, fileName))
				{
					Console.WriteLine($"[Cleaner:{UserId}] Папка {chunkDir} активна (грузится файл {fileName}), пропускаем удаление.");
					continue;
				}

				var existing = await _dbContext.ChunkStatuses
					.FirstOrDefaultAsync(c => c.UserId == UserId && c.ChunkFolder == folderName);

				Console.WriteLine($"[Cleaner:{UserId}] Проверка папки {chunkDir}, active={isActive}, chunkCount={Directory.GetFiles(chunkDir).Length}");

				if (existing != null)
				{
					existing.TotalBytes = chunkSize;
					existing.IsActive = isActive;
					existing.UpdatedAt = DateTime.UtcNow;
				}
				else
				{
					_dbContext.ChunkStatuses.Add(new ChunkStatus
					{
						UserId = UserId,
						ChunkFolder = folderName,
						TotalBytes = chunkSize,
						IsActive = isActive,
						UpdatedAt = DateTime.UtcNow
					});
				}

				if (!isActive)
				{
					try
					{
						Directory.Delete(chunkDir, true);
						_uploadTaskService.RemoveActiveTask(chunkDir);
						Console.WriteLine($"[Cleaner:{UserId}] Успешно удалена неактивная папка {chunkDir}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[Cleaner:{UserId}] Ошибка при удалении {chunkDir}: {ex}");
					}
				}
			}

			await _dbContext.SaveChangesAsync();
		}
	}
}