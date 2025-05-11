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

			var list = new List<object>();
			if (Directory.Exists(userFolder))
			{
				foreach (var f in Directory.GetFiles(userFolder))
				{
					var fi = new FileInfo(f);
					var name = fi.Name;
					if (name.EndsWith(".ini") || name.StartsWith("history_")) continue;
					list.Add(new
					{
						name,
						sizeMb = Math.Round(fi.Length / 1048576.0, 2),
						lastWriteTime = fi.LastWriteTime.ToString("dd.MM.yyyy, HH:mm"),
						version = "1.0"
					});
				}
			}
			await Clients.Caller.SendAsync("ReceiveStorageUpdate", list);
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
