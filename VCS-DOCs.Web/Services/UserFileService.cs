using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Upload.Core.Models;
using VCS_DOCs.Upload.Core.Services;
using VCS_DOCs.Data;

namespace VCS_DOCs.Services
{
	public class UserFileService : IUserFileService
	{
		private readonly ApplicationDbContext _db;

		public UserFileService(ApplicationDbContext db)
		{
			_db = db;
		}

		public async Task<List<UserFileDto>> GetFilesForUserAsync(string userId)
		{
			return await _db.FileUploadSessions
				.Where(s => s.UserId == userId && s.Status == "complete")
				.GroupBy(s => s.FileId)
				.Select(g => new UserFileDto
				{
					FileId = g.Key,
					FileName = g.OrderByDescending(s => s.UpdatedAt).First().OriginalFileName,
					FileSize = g.OrderByDescending(s => s.UpdatedAt).First().FileSize,
					UpdatedAt = g.OrderByDescending(s => s.UpdatedAt).First().UpdatedAt,
					LatestVersion = g.Max(s => s.Version),
					Versions = g.Select(s => new VersionDto
					{
						Version = s.Version,
						UploadedAt = s.UpdatedAt
					}).ToList()
				})
				.ToListAsync();
		}
	}
}