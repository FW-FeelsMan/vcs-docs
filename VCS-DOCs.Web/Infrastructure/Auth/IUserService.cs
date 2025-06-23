namespace VCS_DOCs.Infrastructure.Auth
{
	public interface IUserService
	{
		Task UpdateUserStatusAsync(string userId, bool isOnline);
		Task ClearUserJwtIdAsync(string userId);
	}
}
