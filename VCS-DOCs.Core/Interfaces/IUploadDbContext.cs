using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Core.Interfaces
{
	public interface IUploadDbContext
	{
		DbSet<FileUploadSessionModel> FileUploadSessions { get; }
		DbSet<ServerSettingModel> ServerSettings { get; }
        DbSet<SharedLink> SharedLinks
        {
            get; set;
        }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        ChangeTracker ChangeTracker
        {
            get;
        }
    }
}