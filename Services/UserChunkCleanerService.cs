using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public class UserChunkCleanerService : IUserMicroservice
	{
		private readonly string _userDataPath;
		private readonly FileUploadTaskService _uploadTaskService;
		private readonly UserStorageQuotaService _quotaService;
		private readonly string _username;
		private CancellationTokenSource _cts;
		private Task _backgroundTask;

		public string UserId { get; }
		public bool ShouldKeepRunningAfterUserDisconnect => false;

		public UserChunkCleanerService(
			string userId,
			string username,
			string userDataPath,
			FileUploadTaskService uploadTaskService,
			UserStorageQuotaService quotaService)
		{
			UserId = userId;
			_username = username;
			_userDataPath = userDataPath;
			_uploadTaskService = uploadTaskService;
			_quotaService = quotaService;
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
			var sb = new StringBuilder();

			foreach (var chunkDir in chunkDirs)
			{
				bool isActive = _uploadTaskService.IsTaskActiveForFolder(chunkDir);
				long chunkSize = Directory.GetFiles(chunkDir).Sum(f => new FileInfo(f).Length);
				string chunkFolderName = Path.GetFileName(chunkDir);

				sb.AppendLine($"{chunkFolderName}={chunkSize},{(isActive ? "active" : "inactive")}");

				if (!isActive)
				{
					try
					{
						Directory.Delete(chunkDir, true);
						_uploadTaskService.RemoveActiveTask(chunkDir);
						_quotaService.ReleaseFileReservation(UserId, chunkFolderName);
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"[Cleaner:{UserId}] Ошибка при удалении {chunkDir}: {ex}");
					}
				}
			}

			string iniPath = Path.Combine(_userDataPath, $"history_{_username}.ini");
			var iniContent = new StringBuilder();
			iniContent.AppendLine("[Chunks]");
			iniContent.Append(sb.ToString());

			File.WriteAllText(iniPath, iniContent.ToString());

			await Task.CompletedTask;
		}
	}
}