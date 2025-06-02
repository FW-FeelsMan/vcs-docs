using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;

namespace VCS_DOCs.Services.User
{
	public class UserServiceManager
	{
		private readonly UserDataPathOptions _options;

		public UserServiceManager(IOptions<UserDataPathOptions> options)
		{
			_options = options.Value;
		}

		// Заглушка, потому что раньше здесь запускались микросервисы
		public void StartUserServices(string userId, string username)
		{
			// Теперь это просто имитация великой деятельности
			Console.WriteLine($"[UserServiceManager] Старт сервисов для {username} ({userId}) — на самом деле ничего не делаем.");
		}

		// Заглушка, потому что раньше тут их останавливали
		public Task StopUserServicesAsync(string userId)
		{
			Console.WriteLine($"[UserServiceManager] Стоп сервисов для {userId} — на самом деле ничего не делаем.");
			return Task.CompletedTask;
		}
	}
}
