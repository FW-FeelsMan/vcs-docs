using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VCS_DOCs.Services.Tasks;

namespace VCS_DOCs.Hubs
{
	public class TaskHub : Hub {
		private readonly TaskPushService _taskPush;

		public TaskHub(TaskPushService taskPush)
		{
			_taskPush = taskPush;
		}

		public override async Task OnConnectedAsync()
		{
			var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId))
			{
				await _taskPush.PushTasksToUserAsync(userId);
				Console.WriteLine($"userId найден: {userId}");
			}
			else
			{
				Console.WriteLine("Context.User пустой! Печаль.");
			}
			await base.OnConnectedAsync();
		}
	}
}
