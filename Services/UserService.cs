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
			var user = await _context.Users.FindAsync(int.Parse(userId));
			if (user != null)
			{
				user.StatusOnline = isOnline ? 1 : 0;
				user.LastEntry = DateTime.UtcNow;
				_context.Users.Update(user);
				await _context.SaveChangesAsync();
			}
		}
		public async Task ClearUserJwtIdAsync(string userId)
		{
			if (int.TryParse(userId, out var parsedUserId))
			{
				var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == parsedUserId);
				if (user != null)
				{
					user.JwtId = null;
					await _context.SaveChangesAsync();
				}
			}
			else
			{
				Console.WriteLine("Ошибка преобразования userId в int.");
			}
		}
	}
}
