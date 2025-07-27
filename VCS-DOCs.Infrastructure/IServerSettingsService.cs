namespace VCS_DOCs.Infrastructure
{
	public interface IServerSettingsService
	{
		Task<string?> GetValueAsync(string key);
		Task<int> GetRamDiskSizeGbAsync();
	}
}
