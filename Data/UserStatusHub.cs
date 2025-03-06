using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VCS_DOCs.Services;

namespace VCS_DOCs.Data
{
	[Authorize]
	public class UserStatusHub : Hub
	{
		private readonly IUserService _userService;

		public UserStatusHub(IUserService userService)
		{
			_userService = userService;
		}

		public override async Task OnConnectedAsync()
		{
			var user = Context.User;
			var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
			if (userIdClaim != null)
			{
				var userId = userIdClaim.Value;
				await _userService.UpdateUserStatusAsync(userId, true);
				Console.WriteLine($"Статус пользователя {userId} обновлен на 'онлайн'.");
			}
			else
			{
				Console.WriteLine("Утверждение NameIdentifier не найдено.");
			}
			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception exception)
		{
			var user = Context.User;
			var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
			if (userIdClaim != null)
			{
				var userId = userIdClaim.Value;
				await _userService.UpdateUserStatusAsync(userId, false);
				Console.WriteLine($"Статус пользователя {userId} обновлен на 'оффлайн'.");
			}
			else
			{
				Console.WriteLine("Утверждение NameIdentifier не найдено.");
			}
			await base.OnDisconnectedAsync(exception);
		}
	}
}
