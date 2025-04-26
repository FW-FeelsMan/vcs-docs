namespace VCS_DOCs.Services.User
{
	public interface IUserService
	{
		Task UpdateUserStatusAsync(string userId, bool isOnline);
		Task ClearUserJwtIdAsync(string userId);
	}
}
