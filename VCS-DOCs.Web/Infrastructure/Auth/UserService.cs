using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;

namespace VCS_DOCs.Infrastructure.Auth
{
	public class UserService : IUserService
	{
		private readonly ApplicationDbContext _context;

		public UserService(ApplicationDbContext context)
		{
			_context = context;
		}

		public async Task UpdateUserStatusAsync(string userId, bool isOnline)
		{
			var user = await _context.Users.FindAsync(userId);
			if (user != null)
			{
				user.StatusOnline = isOnline ? 1 : 0;
				user.LastEntry = DateTime.Now;
				await _context.SaveChangesAsync();
			}
		}

		public async Task ClearUserJwtIdAsync(string userId)
		{
			var user = await _context.Users.FindAsync(userId);
			if (user != null)
			{
				user.JwtId = null;
				await _context.SaveChangesAsync();
			}
			else
			{
				Console.WriteLine($"Пользователь с Id='{userId}' не найден.");
			}
		}
	}
}
