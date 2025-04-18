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
		private readonly ConcurrentDictionary<string, UserBackgroundService> _userServices = new();
		private readonly ConcurrentDictionary<string, UserStorageMonitoringService> _userStorageServices = new();

		public UserServiceManager(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public UserBackgroundService GetOrCreateService(string userId, string username)
		{
			if (_userServices.ContainsKey(userId))
			{
				Console.WriteLine($"[UserServiceManager] Микросервис для пользователя {userId} уже запущен и продолжает работу.");
				return _userServices[userId];
			}

			var logger = _serviceProvider.GetRequiredService<ILogger<UserBackgroundService>>();
			var cleanerLogger = _serviceProvider.GetRequiredService<ILogger<UserChunkCleanerService>>();
			var quotaService = _serviceProvider.GetRequiredService<UserStorageQuotaService>();
			var fileUploadTaskService = _serviceProvider.GetRequiredService<FileUploadTaskService>();
			var env = _serviceProvider.GetRequiredService<IWebHostEnvironment>();

			var service = new UserBackgroundService(userId, logger);
			_userServices[userId] = service;
			Task.Run(() => service.StartAsync(CancellationToken.None));

			var userDataPath = Path.Combine(env.ContentRootPath, "Data", "userData", $"userData_{username}");

			var chunkCleaner = new UserChunkCleanerService(
				userId,
				userDataPath,
				fileUploadTaskService,
				quotaService,
				cleanerLogger
			);

			Task.Run(() => chunkCleaner.RunAsync(CancellationToken.None));

			Console.WriteLine($"[UserServiceManager] Запущен сервис для пользователя {userId}");
			return service;
		}

		public UserStorageMonitoringService GetOrCreateStorageService(string userId, string userFolderPath)
		{
			if (_userStorageServices.ContainsKey(userId))
			{
				Console.WriteLine($"[UserServiceManager] Сервис мониторинга хранилища для пользователя {userId} уже запущен.");
				return _userStorageServices[userId];
			}

			var logger = _serviceProvider.GetRequiredService<ILogger<UserStorageMonitoringService>>();
			var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.UserStorageHub>>();
			var service = new UserStorageMonitoringService(userId, userFolderPath, logger, hubContext);
			_userStorageServices[userId] = service;
			Task.Run(() => service.StartAsync(CancellationToken.None));
			Console.WriteLine($"[UserServiceManager] Запущен сервис мониторинга хранилища для пользователя {userId}");
			return service;
		}
	}
}
