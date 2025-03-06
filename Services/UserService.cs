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
	}
}
