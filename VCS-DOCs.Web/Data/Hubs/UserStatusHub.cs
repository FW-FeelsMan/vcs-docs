using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Tasks;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Data.Hubs
{
    /// <summary>
    /// Трекинг онлайн-подключений пользователей в памяти + синхронизация статуса в БД.
    /// Также умеет принудительно разлогинивать пользователей.
    /// </summary>
    [Authorize]
    public class UserStatusHub : Hub
    {
        private readonly IUserService _userService;
        private readonly IHubContext<UserStatusHub> _hubContext;

        // userId -> set(connectionId)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);

        public UserStatusHub(
            IUserService userService,
            IHubContext<UserStatusHub> hubContext)
        {
            _userService = userService;
            _hubContext = hubContext;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = Context.User?.Identity?.Name;

            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userName))
            {
                var conns = _connections.GetOrAdd(userId,
                    _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                conns[Context.ConnectionId] = 0;

                // первый коннект этого пользователя — считаем его онлайн
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

                // последний коннект ушёл — выставляем оффлайн + инвалидируем sid/JwtId
                if (conns.IsEmpty)
                {
                    _connections.TryRemove(userId, out _);
                    await _userService.UpdateUserStatusAsync(userId, false);
                    await _userService.ClearUserJwtIdAsync(userId);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Быстрая проверка онлайна по локальному in-memory словарю.
        /// НУЖНА для SupportBridgeController /presence.
        /// </summary>
        public static bool IsOnlineUser(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            return _connections.TryGetValue(userId, out var conns) && !conns.IsEmpty;
        }

        /// <summary>
        /// Принудительный выход пользователя на стороне V-DOCs:
        /// шлём событие "ForceLogout" и сразу инвалидируем JwtId в БД.
        /// </summary>
        public async Task ForceLogoutUser(string userId)
        {
            await _hubContext.Clients.User(userId).SendAsync("ForceLogout");
            await _userService.ClearUserJwtIdAsync(userId);
        }
    }
}
