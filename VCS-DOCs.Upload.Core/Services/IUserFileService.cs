using VCS_DOCs.Upload.Core.Models;

namespace VCS_DOCs.Upload.Core.Services
{
	public interface IUserFileService
	{
		Task<List<UserFileDto>> GetFilesForUserAsync(string userId);
	}
}