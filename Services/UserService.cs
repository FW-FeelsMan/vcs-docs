using Microsoft.EntityFrameworkCore;

namespace VCS_DOCs.Services
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
			// теперь просто ищем по строковому ключу
			var user = await _context.Users.FindAsync(userId);
			if (user != null)
			{
				user.StatusOnline = isOnline ? 1 : 0;
				user.LastEntry = DateTime.UtcNow;
				await _context.SaveChangesAsync();
			}
		}

		public async Task ClearUserJwtIdAsync(string userId)
		{
			// опять-таки просто ищем пользователя по строковому Id
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
