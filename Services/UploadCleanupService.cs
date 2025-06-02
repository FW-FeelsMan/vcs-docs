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

			await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
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

		if (sessions.Count == 0)
		{
			//_logger.LogInformation("Нет сессий для очистки со статусом '{Status}'", status);
			return;
		}

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
				await _hub.Clients.User(session.UserId).SendAsync("TaskUpdate", new
				{
					title = $"Очищена сессия со статусом '{status}': {session.OriginalFileName}",
					type = "system",
					statusClass = "done",
					statusText = "Завершено",
					cancelable = false
				});
			}
		}

		await db.SaveChangesAsync();
	}

	private string GetTempDir(string userId, string fileHash)
	{
		var shortId = userId.Replace("-", "").Substring(0, 8);
		return Path.Combine("Data", "userData", $"u_{shortId}", "temp", fileHash);
	}
}
