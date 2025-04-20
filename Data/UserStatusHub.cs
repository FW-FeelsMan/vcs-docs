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
		private readonly IHubContext<UserStatusHub> _hubContext;
		private readonly UserServiceManager _userServiceManager;

		public UserStatusHub(
			IUserService userService,
			IHubContext<UserStatusHub> hubContext,
			UserServiceManager userServiceManager)
		{
			_userService = userService;
			_hubContext = hubContext;
			_userServiceManager = userServiceManager;
		}

		public override async Task OnConnectedAsync()
		{
			var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
			var username = Context.User?.Identity?.Name;

			if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username))
			{
				await _userService.UpdateUserStatusAsync(userId, true);
				await _hubContext.Clients.User(userId).SendAsync("InvalidateOtherSessions");

				// Новый вызов запуска всех микросервисов
				_userServiceManager.StartUserServices(userId, username);
			}

			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception? exception)
		{
			var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId))
			{
				try
				{
					await _userService.UpdateUserStatusAsync(userId, false);
					await _userService.ClearUserJwtIdAsync(userId);

					Console.WriteLine($"[Hub] User {userId} отключился — остановка сервисов.");
					await _userServiceManager.StopUserServicesAsync(userId);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ошибка при обновлении статуса пользователя {userId}: {ex.Message}");
				}
			}
			await base.OnDisconnectedAsync(exception);
		}

		public async Task ForceLogoutUser(string userId)
		{
			Console.WriteLine($"Force logout initiated for user {userId}");
			await _hubContext.Clients.User(userId).SendAsync("ForceLogout");
			await _userService.ClearUserJwtIdAsync(userId);
			Console.WriteLine($"JwtId cleared for user {userId}");
		}

		public async Task DebugMessage()
		{
			await Clients.Caller.SendAsync("DebugResponse", "Проверка связи с сервером успешна");
		}
	}
}