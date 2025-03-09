using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace VCS_DOCs.Services
{
	public class UserServiceManager
	{
		private readonly IServiceProvider _serviceProvider;
		private readonly ConcurrentDictionary<string, UserBackgroundService> _userServices = new();

		public UserServiceManager(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public UserBackgroundService GetOrCreateService(string userId)
		{
			if (_userServices.ContainsKey(userId))
			{
				// Если сервис для пользователя уже существует, выводим сообщение и возвращаем текущий сервис
				Console.WriteLine($"[UserServiceManager] Микросервис для пользователя {userId} уже запущен и продолжает работу.");
				return _userServices[userId];
			}

			// Создаем новый сервис, если его нет
			var logger = _serviceProvider.GetRequiredService<ILogger<UserBackgroundService>>();
			var service = new UserBackgroundService(userId, logger);
			_userServices[userId] = service;
			Task.Run(() => service.StartAsync(CancellationToken.None));
			Console.WriteLine($"[UserServiceManager] Запущен сервис для пользователя {userId}");
			return service;
		}
	}
}
