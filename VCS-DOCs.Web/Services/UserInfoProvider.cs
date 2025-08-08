using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Data;

namespace VCS_DOCs.Infrastructure.Services
{
    public class UserInfoProvider : IUserInfoProvider
    {
        private readonly ApplicationDbContext _db;

        public UserInfoProvider(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<long> GetUserStorageLimitAsync(string shortUserId)
        {
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id.Replace("-", "").StartsWith(shortUserId));
            return user?.StorageLimitBytes ?? 10L * 1024 * 1024 * 1024;
        }

        public string ResolveFullUserId(string shortUserId)
        {
            var id = _db.Users
                .AsNoTracking()
                .Where(u => u.Id.Replace("-", "").StartsWith(shortUserId))
                .Select(u => u.Id)
                .FirstOrDefault();
            return id ?? shortUserId;
        }
    }
}
