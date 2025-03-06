namespace VCS_DOCs.Services
{
	public interface IUserService
	{
		Task UpdateUserStatusAsync(string userId, bool isOnline);
	}
}
