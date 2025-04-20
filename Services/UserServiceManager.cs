using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public class UserServiceManager
	{
		private readonly IServiceProvider _serviceProvider;

		private readonly ConcurrentDictionary<string, List<IUserMicroservice>> _microservices = new();

		public UserServiceManager(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public void StartUserServices(string userId, string username)
		{
			if (_microservices.ContainsKey(userId))
			{
				Console.WriteLine($"[UserServiceManager] Сервисы для пользователя {userId} уже запущены.");
				return;
			}

			// Получаем экземпляр quotaService здесь
			var quotaService = _serviceProvider.GetRequiredService<UserStorageQuotaService>();

			quotaService.RegisterUser(userId, username);

			var services = new List<IUserMicroservice>();

			var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.UserStorageHub>>();
			var env = _serviceProvider.GetRequiredService<IWebHostEnvironment>();
			string userFolder = Path.Combine(env.ContentRootPath, "Data", "userData", $"userData_{username}");

			var storageMonitor = new UserStorageMonitoringService(userId, userFolder, hubContext);
			services.Add(storageMonitor);

			var uploadService = _serviceProvider.GetRequiredService<FileUploadTaskService>();
			var chunkCleaner = new UserChunkCleanerService(userId, username, userFolder, uploadService, quotaService);
			services.Add(chunkCleaner);

			_microservices[userId] = services;

			foreach (var service in services)
			{
				Task.Run(() => service.StartAsync(CancellationToken.None));
			}

			Console.WriteLine($"[UserServiceManager] Запущены все микросервисы для пользователя {userId}");
		}

		public async Task StopUserServicesAsync(string userId)
		{
			if (!_microservices.TryGetValue(userId, out var services)) return;

			foreach (var service in services)
			{
				await service.DelayAndStopAsync();
			}

			_microservices.TryRemove(userId, out _);

			Console.WriteLine($"[UserServiceManager] Все микросервисы пользователя {userId} остановлены");
		}
	}
}
