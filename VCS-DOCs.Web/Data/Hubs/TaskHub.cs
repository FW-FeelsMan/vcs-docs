using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace VCS_DOCs.Hubs
{
	public class TaskHub : Hub
	{
		public override async Task OnConnectedAsync()
		{
			var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId))
			{
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
