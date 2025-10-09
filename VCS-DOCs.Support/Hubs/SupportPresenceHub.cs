using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Support.Hubs
{
    [Authorize] // доступен всем авторизованным (входит и BaseUser)
    public class SupportPresenceHub : Hub
    {
        private readonly IUserService _userService;

        // userId -> set(connectionId)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections =
            new(StringComparer.Ordinal);

        public SupportPresenceHub(IUserService userService) => _userService = userService;

        private static string GroupFor(string userId) => $"presence:{userId}";

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var conns = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                conns[Context.ConnectionId] = 0;

                if (conns.Count == 1)
                {
                    // первый коннект пользователя на сервере
                    await _userService.UpdateUserStatusAsync(userId, true);
                    await Clients.Group(GroupFor(userId))
                                 .SendAsync("Presence", new
                                 {
                                     userId,
                                     online = true
                                 });
                }
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
                    // ждём возможный auto-reconnect
                    await Task.Delay(5000);
                    if (_connections.TryGetValue(userId, out var check) && !check.IsEmpty)
                    {
                        await base.OnDisconnectedAsync(exception);
                        return;
                    }

                    _connections.TryRemove(userId, out _);
                    await _userService.UpdateUserStatusAsync(userId, false);
                    await Clients.Group(GroupFor(userId))
                                 .SendAsync("Presence", new
                                 {
                                     userId,
                                     online = false
                                 });
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// Клиент подписывается на список юзеров и получает моментальный снимок статусов.
        public async Task WatchUsers(string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;

            var ids = userIds.Where(s => !string.IsNullOrWhiteSpace(s))
                             .Select(s => s.Trim())
                             .Distinct(StringComparer.Ordinal)
                             .ToArray();

            foreach (var id in ids)
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id));

            // мгновенный снимок по каждому наблюдаемому
            foreach (var id in ids)
            {
                var online = _connections.TryGetValue(id, out var set) && set is { IsEmpty: false } == true;
                await Clients.Caller.SendAsync("Presence", new { userId = id, online });
            }
        }

        /// Пинг от клиента — продлеваем online текущему пользователю.
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