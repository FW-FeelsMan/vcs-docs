using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Support.Hubs
{
    /// <summary>
    /// Трекает подключения, обновляет флаг онлайна в БД,
    /// рассылает presence-ивенты и даёт снапшоты по запросу.
    /// </summary>
    [Authorize(Policy = "SupportDeskAccess")]
    public class SupportPresenceHub : Hub
    {
        private readonly IUserService _userService;

        // userId -> set(connectionId)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections =
            new(StringComparer.Ordinal);

        public SupportPresenceHub(IUserService userService) => _userService = userService;

        private static bool IsOnline(string userId) =>
            _connections.TryGetValue(userId, out var conns) && conns is { IsEmpty: false };

        private Task BroadcastPresence(string userId, bool online) =>
            Clients.Group($"watch:{userId}")
                   .SendAsync("Presence", new
                   {
                       userId,
                       online
                   });

        private async Task SendSnapshotToCaller(IEnumerable<string> userIds)
        {
            foreach (var id in userIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
                await Clients.Caller.SendAsync("Presence", new { userId = id, online = IsOnline(id) });
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var conns = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                conns[Context.ConnectionId] = 0;

                if (conns.Count == 1)
                {
                    await _userService.UpdateUserStatusAsync(userId, true);
                    await BroadcastPresence(userId, true);
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
                    // даём шанс на быстрое переподключение (F5/шатания сети)
                    await Task.Delay(5000);

                    if (_connections.TryGetValue(userId, out var check) && !check.IsEmpty)
                    {
                        await base.OnDisconnectedAsync(exception);
                        return;
                    }

                    _connections.TryRemove(userId, out _);
                    await _userService.UpdateUserStatusAsync(userId, false);
                    await BroadcastPresence(userId, false);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        // ----- API -----

        public async Task WatchUsers(IEnumerable<string> userIds)
        {
            var ids = (userIds ?? Enumerable.Empty<string>())
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .Distinct(StringComparer.Ordinal)
                      .ToArray();

            foreach (var id in ids)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"watch:{id}");

            await SendSnapshotToCaller(ids);
        }

        public async Task Watch(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"watch:{userId}");
            await Clients.Caller.SendAsync("Presence", new { userId, online = IsOnline(userId) });
        }

        public async Task UnwatchUsers(IEnumerable<string> userIds)
        {
            foreach (var id in (userIds ?? Enumerable.Empty<string>())
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.Ordinal))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"watch:{id}");
        }

        public Task<IDictionary<string, object>> GetPresenceMany(IEnumerable<string> userIds)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var id in (userIds ?? Enumerable.Empty<string>())
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.Ordinal))
                dict[id] = new { userId = id, online = IsOnline(id) };
            return Task.FromResult<IDictionary<string, object>>(dict);
        }

        public Task<object> GetPresence(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Task.FromResult<object>(new { userId = "", online = false });
            return Task.FromResult<object>(new { userId, online = IsOnline(userId) });
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
