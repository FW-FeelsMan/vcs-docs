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
		private CancellationTokenSource _cts;
		private Task _backgroundTask;
		private readonly string _username;

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
			Console.WriteLine($"[Cleaner:{UserId}] Стоп получен. Запуск контрольной очистки.");
			await RunOneCleanupAsync();
			Console.WriteLine($"[Cleaner:{UserId}] Контрольная очистка завершена.");
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
				sb.AppendLine($"{Path.GetFileName(chunkDir)}={chunkSize},{(isActive ? "active" : "inactive")}");

				if (!isActive)
				{
					try
					{
						Directory.Delete(chunkDir, true);
						_quotaService.ReleaseReservation(UserId, chunkSize);
						_uploadTaskService.RemoveActiveTask(chunkDir);
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.ToString());
					}
				}
			}

			// Перерасчет остатка активных чанков
			long correctedReservedBytes = Directory
				.GetDirectories(_userDataPath, "*_chunks", SearchOption.TopDirectoryOnly)
				.Where(d => _uploadTaskService.IsTaskActiveForFolder(d))
				.SelectMany(d => Directory.GetFiles(d))
				.Sum(f => new FileInfo(f).Length);

			// Обновляем кэш вручную
			_quotaService.ForceSetReservation(UserId, _username, correctedReservedBytes);

			string iniPath = Path.Combine(_userDataPath, $"history_{_username}.ini");

			var iniContent = new StringBuilder();
			iniContent.AppendLine("[Quota]");
			iniContent.AppendLine($"ReservedBytes={correctedReservedBytes}");
			iniContent.AppendLine();
			iniContent.AppendLine("[Chunks]");
			iniContent.Append(sb.ToString());

			File.WriteAllText(iniPath, iniContent.ToString());

			await Task.CompletedTask;
		}
	}
}