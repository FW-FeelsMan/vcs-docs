using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
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
					.Select(file =>
					{
						var match = Regex.Match(file.Name, @"^(.*)\.v(\d+\.\d+)$", RegexOptions.IgnoreCase);
						var baseName = match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(file.Name);
						var version = match.Success ? match.Groups[2].Value : "1.0";

						return new { file, baseName, version };
					})
					.GroupBy(x => x.baseName.ToLowerInvariant());

				foreach (var group in groups)
				{
					var versions = group
						.Select(x => x.version)
						.Distinct()
						.OrderBy(v => Version.Parse(v))
						.ToList();

					var newestFileEntry = group
						.OrderByDescending(x => Version.Parse(x.version))
						.First();

					var entry = new
					{
						baseName = group.Key,
						extension = newestFileEntry.file.Extension,
						displayName = group.Key,
						currentVersion = versions.Last(),
						allVersions = versions,
						sizeMb = Math.Round(newestFileEntry.file.Length / 1048576.0, 2),
						lastWriteTime = newestFileEntry.file.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
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
