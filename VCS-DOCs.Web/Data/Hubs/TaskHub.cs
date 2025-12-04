using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace VCS_DOCs.Hubs;

public sealed class TaskHub : Hub
{
	public override Task OnConnectedAsync()
	{
		var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

		if (!string.IsNullOrWhiteSpace(userId))
			Console.WriteLine($"TaskHub connected: userId={userId}");
		else
			Console.WriteLine("TaskHub connected: unauthenticated (Context.User is null or has no NameIdentifier).");

		return base.OnConnectedAsync();
	}
}
