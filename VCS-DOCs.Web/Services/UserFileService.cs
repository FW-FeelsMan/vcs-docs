using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Upload.Core.Models;
using VCS_DOCs.Upload.Core.Services;

namespace VCS_DOCs.Services;

public sealed class UserFileService : IUserFileService
{
	private const string CompleteStatus = "complete";

	private readonly ApplicationDbContext _db;

	public UserFileService(ApplicationDbContext db) => _db = db;

	public Task<List<UserFileDto>> GetFilesForUserAsync(string userId)
	{
		if (string.IsNullOrWhiteSpace(userId))
			return Task.FromResult(new List<UserFileDto>());

		return _db.FileUploadSessions
			.AsNoTracking()
			.Where(s => s.UserId == userId && s.Status == CompleteStatus)
			.GroupBy(s => s.FileId)
			.Select(g => new
			{
				FileId = g.Key,
				Latest = g.OrderByDescending(x => x.UpdatedAt).FirstOrDefault(),
				LatestVersion = g.Max(x => x.Version),
				Versions = g.Select(x => new VersionDto
				{
					Version = x.Version,
					UploadedAt = x.UpdatedAt
				})
			})
			.Where(x => x.Latest != null)
			.Select(x => new UserFileDto
			{
				FileId = x.FileId,
				FileName = x.Latest!.OriginalFileName,
				FileSize = x.Latest!.FileSize,
				UpdatedAt = x.Latest!.UpdatedAt,
				LatestVersion = x.LatestVersion,
				Versions = x.Versions.OrderByDescending(v => v.Version).ToList()
			})
			.ToListAsync();
	}
}
