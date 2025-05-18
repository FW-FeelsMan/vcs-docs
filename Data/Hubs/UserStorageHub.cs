using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Services.Upload;

namespace VCS_DOCs.Data.Hubs
{
	[Authorize]
	public class UserStorageHub : Hub
	{
		private readonly IWebHostEnvironment _env;

		public UserStorageHub(IWebHostEnvironment env)
		{
			_env = env;
		}

		public override async Task OnConnectedAsync()
		{
			var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrEmpty(userId))
			{
				Context.Abort();
				return;
			}
			await Groups.AddToGroupAsync(Context.ConnectionId, userId);
			await base.OnConnectedAsync();
		}
		public async Task RequestCurrentFiles()
		{
			var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrEmpty(userId))
				return;

			string basePath = Path.Combine(_env.ContentRootPath, "Data", "userData");
			string userFolder = Path.Combine(basePath, $"userData_{userId}");

			var entries = new List<object>();
			if (Directory.Exists(userFolder))
			{
				var files = new DirectoryInfo(userFolder).GetFiles();

				var groups = files
					.GroupBy(file =>
					{
						var nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
						var baseName = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, "_v\\d+\\.0$", "");
						return baseName.ToLowerInvariant();
					});

				foreach (var group in groups)
				{
					var versions = group
						.Select(file =>
						{
							var match = System.Text.RegularExpressions.Regex.Match(file.Name, "_v(\\d+)\\.0", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
							return match.Success ? $"v{match.Groups[1].Value}.0" : "v1.0";
						})
						.OrderBy(v => v)
						.ToList();

					var newestFile = group
						.OrderByDescending(f => f.LastWriteTimeUtc)
						.First();
					var match = System.Text.RegularExpressions.Regex.Match(newestFile.Name, "_v(\\d+)\\.0", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
					var displayName = Path.GetFileNameWithoutExtension(newestFile.Name);

					if (match.Success && !string.IsNullOrEmpty(match.Value))
					{
						displayName = displayName.Replace(match.Value, "");
					}

					var entry = new
					{
						baseName = group.Key,
						extension = newestFile.Extension,
						displayName = displayName,
						currentVersion = versions.LastOrDefault() ?? "v1.0",
						allVersions = versions,
						sizeMb = Math.Round(newestFile.Length / 1048576.0, 2),
						lastWriteTime = newestFile.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
					};

					entries.Add(entry);
				}
			}

			await Clients.Caller.SendAsync("ReceiveStorageUpdate", entries);
		}

		public async Task CancelUpload(string fileName)
		{
			var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return;

			ActiveUploadsRegistry.Unregister(userId, fileName);
			await Clients.Caller.SendAsync("UploadCancelled", new { name = fileName });
		}
	}
}
