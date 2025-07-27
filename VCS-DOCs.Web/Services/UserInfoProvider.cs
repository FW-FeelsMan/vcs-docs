using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Data;

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
            .FirstOrDefaultAsync(u => u.Id.Replace("-", "").StartsWith(shortUserId));

        return user?.StorageLimitBytes ?? 10L * 1024 * 1024 * 1024;
    }
}
