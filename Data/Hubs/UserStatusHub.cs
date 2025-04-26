using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using VCS_DOCs.Services.User;

namespace VCS_DOCs.Data.Hubs
{
	[Authorize]
	public class UserStatusHub : Hub
	{
		private readonly IUserService _userService;
		private readonly IHubContext<UserStatusHub> _hubContext;
		private readonly UserServiceManager _userServiceManager;
		private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();

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
				var conns = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
				conns[Context.ConnectionId] = 0;
				if (conns.Count == 1)
				{
					await _userService.UpdateUserStatusAsync(userId, true);
				}
				_userServiceManager.StartUserServices(userId, username);
			}
			await base.OnConnectedAsync();
		}

		public override async Task OnDisconnectedAsync(Exception? exception)
		{
			var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId) && _connections.TryGetValue(userId, out var conns))
			{
				conns.TryRemove(Context.ConnectionId, out _);
				if (conns.IsEmpty)
				{
					_connections.TryRemove(userId, out _);
					await _userService.UpdateUserStatusAsync(userId, false);
					await _userService.ClearUserJwtIdAsync(userId);
					await _userServiceManager.StopUserServicesAsync(userId);
				}
			}
			await base.OnDisconnectedAsync(exception);
		}

		public async Task ForceLogoutUser(string userId)
		{
			await _hubContext.Clients.User(userId).SendAsync("ForceLogout");
			await _userService.ClearUserJwtIdAsync(userId);
		}

		public async Task DebugMessage()
		{
			await Clients.Caller.SendAsync("DebugResponse", "Проверка связи с сервером успешна");
		}
	}
}
