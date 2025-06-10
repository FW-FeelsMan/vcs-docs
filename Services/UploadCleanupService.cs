using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs;
using VCS_DOCs.Hubs;

public class UploadCleanupService : BackgroundService
{
	private readonly ILogger<UploadCleanupService> _logger;
	private readonly IServiceProvider _services;
	private readonly IHubContext<TaskHub> _hub;

	public static DateTime LastIncompleteRun { get; private set; } = DateTime.MinValue;
	public static DateTime LastCompilingRun { get; private set; } = DateTime.MinValue;

	public UploadCleanupService(
		ILogger<UploadCleanupService> logger,
		IServiceProvider services,
		IHubContext<TaskHub> hub)
	{
		_logger = logger;
		_services = services;
		_hub = hub;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		//_logger.LogInformation("UploadCleanupService запущен.");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await RunIncompleteCleanupAsync();
				await RunCompilingCleanupAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка при автоматической очистке загрузок.");
			}

			await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
		}
	}

	public Task RunCleanupAsync(bool force = false)
	{
		// Ручной запуск обеих
		return Task.WhenAll(
			RunIncompleteCleanupAsync(force),
			RunCompilingCleanupAsync(force)
		);
	}

	public async Task RunIncompleteCleanupAsync(bool force = false)
	{
		var now = DateTime.Now;
		await RunCleanupByStatusAsync("incomplete", now, force);
		LastIncompleteRun = now;
	}

	public async Task RunCompilingCleanupAsync(bool force = false)
	{
		var now = DateTime.Now;
		await RunCleanupByStatusAsync("compiling", now, force);
		LastCompilingRun = now;
	}

	private async Task RunCleanupByStatusAsync(string status, DateTime now, bool force)
	{
		_logger.LogInformation("Очистка по статусу '{Status}' (force = {Force})", status, force);

		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

		var cutoff = now.AddMinutes(-15);

		var sessions = await db.FileUploadSessions
			.Include(s => s.Chunks)
			.Where(s => s.Status == status)
			.ToListAsync();

		if (!force)
			sessions = sessions.Where(s => s.UpdatedAt < cutoff).ToList();

		foreach (var session in sessions)
		{
			var tempDir = GetTempDir(session.UserId, session.FileHash);

			if (Directory.Exists(tempDir))
			{
				try
				{
					Directory.Delete(tempDir, true);
					_logger.LogInformation("Удалена папка: {TempDir}", tempDir);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Не удалось удалить временную папку: {TempDir}", tempDir);
				}
			}
			else
			{
				_logger.LogInformation("Папка не найдена (пропуск): {TempDir}", tempDir);
			}

			db.FileUploadChunks.RemoveRange(session.Chunks);
			db.FileUploadSessions.Remove(session);

			if (!string.IsNullOrEmpty(session.UserId))
			{
				if (!force && (DateTime.Now - session.UpdatedAt) < TimeSpan.FromSeconds(60))
				{
					_logger.LogInformation("Пропущена повторная отправка TaskUpdate для недавно очищенной сессии: {File}", session.OriginalFileName);
					continue;
				}

				await _hub.Clients.User(session.UserId).SendAsync("TaskUpdate", new
				{
					taskKey = $"cleanup_{status}_{session.FileId}_{session.Version}",
					title = $"Очищена сессия со статусом '{status}': {session.OriginalFileName}",
					type = "system",
					statusClass = "done",
					statusText = "Завершено",
					cancelable = false,
					autoRemove = true,
					autoRemoveDelay = 5000
				});
			}
		}

		CleanupOrphanTempDirs(status);

		await db.SaveChangesAsync();
	}

	private void CleanupOrphanTempDirs(string status)
	{
		_logger.LogInformation("Запущена очистка осиротевших temp-директорий для статуса: {Status}", status);

		var baseTempPath = Path.Combine("Data", "userData");
		if (!Directory.Exists(baseTempPath)) return;

		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
		var knownHashes = new HashSet<string>(
			db.FileUploadSessions.Select(s => s.FileHash).ToList()
		);

		foreach (var userDir in Directory.GetDirectories(baseTempPath))
		{
			var tempDir = Path.Combine(userDir, "temp");
			if (!Directory.Exists(tempDir)) continue;

			foreach (var hashDir in Directory.GetDirectories(tempDir))
			{
				var hash = Path.GetFileName(hashDir);

				if (!knownHashes.Contains(hash))
				{
					try
					{
						Directory.Delete(hashDir, true);
						_logger.LogWarning("Удалена осиротевшая temp-папка без сессии: {Path}", hashDir);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Ошибка при удалении осиротевшей папки: {Path}", hashDir);
					}
				}
			}
		}
	}

	private string GetTempDir(string userId, string fileHash)
	{
		var shortId = userId.Replace("-", "").Substring(0, 8);
		return Path.Combine("Data", "userData", $"u_{shortId}", "temp", fileHash);
	}
}
