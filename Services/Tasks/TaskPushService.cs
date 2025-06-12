using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Hubs;

namespace VCS_DOCs.Services.Tasks
{
	public class TaskPushService
	{
		private readonly IHubContext<TaskHub> _hub;
		private readonly IServiceProvider _provider;

		public TaskPushService(IHubContext<TaskHub> hub, IServiceProvider provider)
		{
			_hub = hub;
			_provider = provider;
		}

		public async Task PushTasksToUserAsync(string userId)
		{
			using var scope = _provider.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			var now = DateTime.Now;
			var nextIncomplete = UploadCleanupService.LastIncompleteRun.AddMinutes(15);
			var nextCompiling = UploadCleanupService.LastCompilingRun.AddMinutes(15);

			var tasks = new List<object>
			{
				new {
					title = "Очистка INCOMPLETE",
					statusText = FormatTime((int)(nextIncomplete - now).TotalSeconds),
					statusClass = "waiting",
					type = "system",
					cancelable = false,
					manualTrigger = true,
					taskKey = "uploadCleanup_incomplete",
					nextRunUtc = nextIncomplete.ToString("o")
				},
				new {
					title = "Очистка COMPILING",
					statusText = FormatTime((int)(nextCompiling - now).TotalSeconds),
					statusClass = "waiting",
					type = "system",
					cancelable = false,
					manualTrigger = true,
					taskKey = "uploadCleanup_compiling",
					nextRunUtc = nextCompiling.ToString("o")
				},
				new {
					taskKey = "singleDeviceControl",
					title = "Контроль входа с одного устройства",
					statusText = "Активна",
					statusClass = "active",
					type = "system",
					cancelable = false
				}
			};

			foreach (var task in tasks)
			{
				await _hub.Clients.User(userId).SendAsync("TaskUpdate", task);
			}
		}

		private string FormatTime(int seconds)
		{
			var mins = seconds / 60;
			var secs = seconds % 60;
			return $"{mins}:{secs.ToString("D2")}";
		}
	}
}
