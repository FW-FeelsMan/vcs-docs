// Hubs/UserStatusHub.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Hubs
{
    [Authorize(Policy = "SupportDeskAccess")]
    public class UserStatusHub : Hub
    {
        private readonly ApplicationDbContext _db;
        public UserStatusHub(ApplicationDbContext db)
        {
            _db = db;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                _db.SupportUserConnections.Add(new SupportUserConnection
                {
                    ConnectionId = Context.ConnectionId,
                    UserId = userId,
                    ConnectedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO SupportUserSessions(UserId, JwtId, IsOnline, LastSeenUtc)
VALUES ({userId}, NULL, 1, {DateTime.UtcNow})
ON CONFLICT(UserId) DO UPDATE SET IsOnline = 1, LastSeenUtc = {DateTime.UtcNow};");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            var conn = await _db.SupportUserConnections.FindAsync(Context.ConnectionId);
            if (conn != null)
            {
                var userId = conn.UserId;
                _db.SupportUserConnections.Remove(conn);
                await _db.SaveChangesAsync();

                var hasAny = await _db.SupportUserConnections
                    .AsNoTracking()
                    .AnyAsync(c => c.UserId == userId);

                if (!hasAny)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {DateTime.UtcNow}
WHERE UserId = {userId};");
                }
            }
            await base.OnDisconnectedAsync(ex);
        }

        public Task ForceLogout() // клиент закроет соединение сам
            => Clients.Caller.SendAsync("ForceLogout");
    }
}
