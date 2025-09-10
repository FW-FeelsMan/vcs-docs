using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Support.Hubs
{
    /// <summary>
    /// Хаб, который:
    /// 1) трекает подключения пользователей (в памяти),
    /// 2) обновляет флаг онлайна в БД через IUserService,
    /// 3) умеет принудительно разлогинить пользователя (шлёт клиенту событие "ForceLogout").
    /// </summary>
    [Authorize]
    public class SupportPresenceHub : Hub
    {
        private readonly IUserService _userService;

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections =
            new(StringComparer.Ordinal);

        public SupportPresenceHub(IUserService userService) => _userService = userService;

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var conns = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                conns[Context.ConnectionId] = 0;

                if (conns.Count == 1)
                    await _userService.UpdateUserStatusAsync(userId, true);
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
                    // даём окну шанс переподключиться после F5
                    await Task.Delay(5000);

                    // если успел переподключиться — ничего не делаем
                    if (_connections.TryGetValue(userId, out var check) && !check.IsEmpty)
                    {
                        await base.OnDisconnectedAsync(exception);
                        return;
                    }

                    _connections.TryRemove(userId, out _);
                    await _userService.UpdateUserStatusAsync(userId, false);

                }
            }
            await base.OnDisconnectedAsync(exception);
        }
        public Task Pulse()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Task.CompletedTask;
            return _userService.UpdateUserStatusAsync(userId, true);
        }

        public async Task ForceLogoutUser(string userId)
        {
            await Clients.User(userId).SendAsync("ForceLogout");
            await _userService.ClearUserJwtIdAsync(userId); 
        }
    }
}
