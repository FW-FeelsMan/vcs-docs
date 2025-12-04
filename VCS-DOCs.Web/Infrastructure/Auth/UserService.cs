using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Infrastructure.Auth;

public sealed class UserService : IUserService
{
	private readonly ApplicationDbContext _context;
	private readonly ILogger<UserService> _log;

	public UserService(ApplicationDbContext context, ILogger<UserService> log)
	{
		_context = context;
		_log = log;
	}

	public async Task UpdateUserStatusAsync(string userId, bool isOnline)
	{
		if (string.IsNullOrWhiteSpace(userId))
			return;

		var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null)
		{
			_log.LogWarning("UpdateUserStatusAsync: user not found. Id={UserId}", userId);
			return;
		}

		user.StatusOnline = isOnline ? 1 : 0;
		user.LastEntry = DateTime.UtcNow;

		await _context.SaveChangesAsync();
	}

	public async Task ClearUserJwtIdAsync(string userId)
	{
		if (string.IsNullOrWhiteSpace(userId))
			return;

		var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null)
		{
			_log.LogWarning("ClearUserJwtIdAsync: user not found. Id={UserId}", userId);
			return;
		}

		user.JwtId = null;
		await _context.SaveChangesAsync();
	}
}
