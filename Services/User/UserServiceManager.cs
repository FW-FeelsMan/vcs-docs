using System.Collections.Concurrent;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Services.Upload;
using VCS_DOCs.Services.Microservices;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace VCS_DOCs.Services.User
{
	public class UserServiceManager
	{
		private readonly IServiceProvider _serviceProvider;
		private readonly UserDataPathOptions _options;

		private readonly ConcurrentDictionary<string, (IServiceScope Scope, List<IUserMicroservice> Services)> _microservices = new();

		public UserServiceManager(IServiceProvider serviceProvider, IOptions<UserDataPathOptions> options)
		{
			_serviceProvider = serviceProvider;
			_options = options.Value;
		}

		public void StartUserServices(string userId, string username)
		{
			if (_microservices.ContainsKey(userId)) return;

			var scope = _serviceProvider.CreateScope();
			var scopedProvider = scope.ServiceProvider;

			var services = new List<IUserMicroservice>();
			var hubContext = scopedProvider.GetRequiredService<IHubContext<UserStorageHub>>();
			var dbContext = scopedProvider.GetRequiredService<ApplicationDbContext>();
			var uploadService = scopedProvider.GetRequiredService<FileUploadTaskService>();

			string userFolder = Path.Combine(_options.BasePath, $"userData_{userId}");

			services.Add(new UserStorageMonitoringService(userId, userFolder, hubContext));
			services.Add(new UserChunkCleanerService(userId, userFolder, uploadService, dbContext));

			_microservices[userId] = (scope, services);

			foreach (var service in services)
				Task.Run(() => service.StartAsync(CancellationToken.None));
		}

		public async Task StopUserServicesAsync(string userId)
		{
			if (!_microservices.TryRemove(userId, out var entry)) return;

			foreach (var service in entry.Services)
			{
				await service.DelayAndStopAsync();
			}

			entry.Scope.Dispose();
		}
	}
}
