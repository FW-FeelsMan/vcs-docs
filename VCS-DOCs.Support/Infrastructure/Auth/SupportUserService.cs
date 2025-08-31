using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Infrastructure.Auth
{
    /// <summary>
    /// Локальная реализация IUserService для саппорта.
    /// Обновляет таблицу SupportUserSessions.
    /// </summary>
    public sealed class SupportUserService : IUserService
    {
        private readonly ApplicationDbContext _db;
        public SupportUserService(ApplicationDbContext db) => _db = db;

        public async Task UpdateUserStatusAsync(string userId, bool isOnline)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;

            var row = await _db.SupportUserSessions.FirstOrDefaultAsync(s => s.UserId == userId);
            if (row == null)
            {
                row = new SupportUserSession
                {
                    UserId = userId,
                    IsOnline = isOnline,
                    LastSeenUtc = DateTime.UtcNow,
                    JwtId = null
                };
                _db.SupportUserSessions.Add(row);
            }
            else
            {
                row.IsOnline = isOnline;
                row.LastSeenUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        public async Task ClearUserJwtIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;

            var row = await _db.SupportUserSessions.FirstOrDefaultAsync(s => s.UserId == userId);
            if (row != null)
            {
                row.JwtId = null;
                await _db.SaveChangesAsync();
            }
        }
    }
}
