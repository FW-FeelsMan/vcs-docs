using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Data.Hubs;

[Authorize]
public sealed class UserStatusHub : Hub
{
	private readonly IUserService _userService;

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Connections =
		new(StringComparer.Ordinal);

	public UserStatusHub(IUserService userService)
	{
		_userService = userService;
	}

	public override async Task OnConnectedAsync()
	{
		var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
		{
			await base.OnConnectedAsync();
			return;
		}

		var conns = Connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
		conns[Context.ConnectionId] = 0;

		if (conns.Count == 1)
			await _userService.UpdateUserStatusAsync(userId, true);

		await base.OnConnectedAsync();
	}

	public override async Task OnDisconnectedAsync(Exception? exception)
	{
		var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
		{
			await base.OnDisconnectedAsync(exception);
			return;
		}

		if (Connections.TryGetValue(userId, out var conns))
		{
			conns.TryRemove(Context.ConnectionId, out _);

			if (conns.IsEmpty)
			{
				Connections.TryRemove(userId, out _);
				await _userService.UpdateUserStatusAsync(userId, false);
				await _userService.ClearUserJwtIdAsync(userId);
			}
		}

		await base.OnDisconnectedAsync(exception);
	}

	public static bool IsOnlineUser(string? userId)
	{
		if (string.IsNullOrWhiteSpace(userId)) return false;
		return Connections.TryGetValue(userId, out var conns) && !conns.IsEmpty;
	}

	public async Task ForceLogoutUser(string userId, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(userId))
			return;

		await Clients.User(userId).SendAsync("ForceLogout", cancellationToken: ct);
		await _userService.ClearUserJwtIdAsync(userId);
	}
}
