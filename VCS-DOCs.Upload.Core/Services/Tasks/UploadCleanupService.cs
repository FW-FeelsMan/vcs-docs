namespace VCS_DOCs.Upload.Core.Services.Tasks
{
	public interface IUploadCleanupService
	{
		Task RunIncompleteCleanupAsync(bool force = false);
		Task RunCompilingCleanupAsync(bool force = false);
		DateTime LastIncompleteRun { get; }
		DateTime LastCompilingRun { get; }
	}

	public class UploadCleanupService : IUploadCleanupService
	{
		private DateTime _lastIncompleteRun;
		private DateTime _lastCompilingRun;

		public Task RunIncompleteCleanupAsync(bool force = false)
		{
			_lastIncompleteRun = DateTime.UtcNow;
			return Task.CompletedTask;
		}

		public Task RunCompilingCleanupAsync(bool force = false)
		{
			_lastCompilingRun = DateTime.UtcNow;
			return Task.CompletedTask;
		}

		public DateTime LastIncompleteRun => _lastIncompleteRun;
		public DateTime LastCompilingRun => _lastCompilingRun;
	}
}
