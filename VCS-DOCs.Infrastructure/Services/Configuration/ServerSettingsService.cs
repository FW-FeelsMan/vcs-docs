using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Infrastructure;
using VCS_DOCs.Models.Entities;

public class ServerSettingsService : IServerSettingsService
{
	private readonly IUploadDbContext _db;
	public DbSet<ServerSettingModel> ServerSettings { get; set; } 
	public ServerSettingsService(IUploadDbContext db)
	{
		_db = db;
	}

	public async Task<string?> GetValueAsync(string key)
	{
		var setting = await _db.ServerSettings
			.FirstOrDefaultAsync(s => s.Key == key);
		return setting?.Value;
	}

	public async Task<int> GetRamDiskSizeGbAsync()
	{
		var value = await GetValueAsync("RamDiskSizeGb");
		return int.TryParse(value, out var gb) ? gb : 0;
	}
}
