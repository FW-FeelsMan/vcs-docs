using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Infrastructure.Services;

public sealed class UserInfoProvider : IUserInfoProvider
{
	private const long DefaultStorageLimitBytes = 10L * 1024 * 1024 * 1024;

	private readonly ApplicationDbContext _db;

	public UserInfoProvider(ApplicationDbContext db) => _db = db;

	public async Task<long> GetUserStorageLimitAsync(string shortUserId)
	{
		if (string.IsNullOrWhiteSpace(shortUserId))
			return DefaultStorageLimitBytes;

		// NOTE: Replace/StartsWith в SQL обычно не индексируется. Лучше хранить ShortUserId в отдельном столбце.
		var user = await _db.Users
			.AsNoTracking()
			.Where(u => u.Id.Replace("-", "").StartsWith(shortUserId))
			.Select(u => new { u.StorageLimitBytes })
			.FirstOrDefaultAsync();

		// если StorageLimitBytes в модели НЕ long/long?, лучше привести тип в User к long.
		return user?.StorageLimitBytes ?? DefaultStorageLimitBytes;
	}

	public string ResolveFullUserId(string shortUserId)
	{
		if (string.IsNullOrWhiteSpace(shortUserId))
			return shortUserId;

		var id = _db.Users
			.AsNoTracking()
			.Where(u => u.Id.Replace("-", "").StartsWith(shortUserId))
			.Select(u => u.Id)
			.FirstOrDefault();

		return id ?? shortUserId;
	}
}
