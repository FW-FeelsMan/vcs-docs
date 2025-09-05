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

        // userId -> set(connectionId)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);

        public SupportPresenceHub(IUserService userService)
        {
            _userService = userService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var conns = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                conns[Context.ConnectionId] = 0;

                // Первый коннект пользователя — считаем, что он стал "онлайн"
                if (conns.Count == 1)
                {
                    await _userService.UpdateUserStatusAsync(userId, true);
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

                // Последний коннект ушёл — помечаем оффлайн и инвалидируем sid/JwtId в БД
                if (conns.IsEmpty)
                {
                    _connections.TryRemove(userId, out _);
                    await _userService.UpdateUserStatusAsync(userId, false);
                    await _userService.ClearUserJwtIdAsync(userId);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>Быстрая проверка онлайна по in-memory состоянию.</summary>
        public static bool IsOnlineUser(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            return _connections.TryGetValue(userId, out var conns) && !conns.IsEmpty;
        }

        /// <summary>
        /// Принудительный выход конкретного пользователя.
        /// Клиентский фронт должен слушать событие "ForceLogout" и выполнять логаут.
        /// </summary>
        public async Task ForceLogoutUser(string userId)
        {
            await Clients.User(userId).SendAsync("ForceLogout");
            await _userService.ClearUserJwtIdAsync(userId);
        }
    }
}
