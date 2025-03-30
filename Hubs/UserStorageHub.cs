using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace VCS_DOCs.Hubs
{
	[Authorize]
	public class UserStorageHub : Hub
	{
		private readonly IWebHostEnvironment _webHostEnvironment;
		public UserStorageHub(IWebHostEnvironment webHostEnvironment)
		{
			_webHostEnvironment = webHostEnvironment;
		}

		public override async Task OnConnectedAsync()
		{
			var userId = Context.User?.Identity?.Name;
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
			var userId = Context.User?.Identity?.Name;
			if (string.IsNullOrEmpty(userId))
				return;

			// Формируем путь к личной папке (должен быть согласован с тем, как он формируется на сервере)
			string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
			string userFolderPath = Path.Combine(appDataPath, $"userData_{userId}");

			var fileInfos = new List<object>();
			if (Directory.Exists(userFolderPath))
			{
				foreach (var file in Directory.GetFiles(userFolderPath))
				{
					var fileInfo = new FileInfo(file);
					fileInfos.Add(new
					{
						name = fileInfo.Name,
						sizeMb = Math.Round((double)fileInfo.Length / (1024 * 1024), 2),
						lastWriteTime = fileInfo.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
					});
				}
			}
			await Clients.Caller.SendAsync("ReceiveStorageUpdate", fileInfos);
		}
	}
}
