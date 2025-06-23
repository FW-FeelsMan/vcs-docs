using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Core.Interfaces
{
	public interface IUploadDbContext
	{
		DbSet<FileUploadSessionModel> FileUploadSessions { get; }
		Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
	}
}