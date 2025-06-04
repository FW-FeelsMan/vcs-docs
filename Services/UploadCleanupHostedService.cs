namespace VCS_DOCs.Services
{
	public class UploadCleanupHostedService : BackgroundService
	{
		private readonly IServiceScopeFactory _scopeFactory;
		private readonly ILogger<UploadCleanupHostedService> _logger;

		public UploadCleanupHostedService(
			IServiceScopeFactory scopeFactory,
			ILogger<UploadCleanupHostedService> logger)
		{
			_scopeFactory = scopeFactory;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			//_logger.LogInformation("UploadCleanupHostedService запущен.");

			while (!stoppingToken.IsCancellationRequested)
			{
				using var scope = _scopeFactory.CreateScope();
				var cleanupService = scope.ServiceProvider.GetRequiredService<UploadCleanupService>();

				try
				{
					await cleanupService.RunIncompleteCleanupAsync();
					await cleanupService.RunCompilingCleanupAsync();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Ошибка при выполнении одной из задач очистки загрузок.");
				}

				try
				{
					await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
				}
				catch (TaskCanceledException)
				{
					break;
				}
			}

			_logger.LogInformation("UploadCleanupHostedService остановлен.");
		}
	}
}
