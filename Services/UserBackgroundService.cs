using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VCS_DOCs.Services
{
	public class UserBackgroundService : BackgroundService
	{
		private readonly string _userId;
		private readonly ILogger<UserBackgroundService> _logger;

		public UserBackgroundService(string userId, ILogger<UserBackgroundService> logger)
		{
			_userId = userId;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation($"Запущен микросервис для пользователя {_userId}.");

			while (!stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("Привет, мир!"); // Просто выводим в лог
				await Task.Delay(1000, stoppingToken);
			}

			_logger.LogInformation($"Пользователь {_userId} вышел. Задачи нет. Микросервис остановлен.");
		}
	}
}
