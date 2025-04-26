using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Data.Hubs;

namespace VCS_DOCs.Services.Upload
{
	public class UserFileUploadService
	{
		private readonly IHubContext<UserStorageHub> _hubContext;
		private readonly ILogger<UserFileUploadService> _logger;
		private const long MaxFolderSizeBytes = 10L * 1024 * 1024 * 1024;
		public UserFileUploadService(IHubContext<UserStorageHub> hubContext, ILogger<UserFileUploadService> logger)
		{
			_hubContext = hubContext;
			_logger = logger;
		}
		public async Task<bool> UploadFileAsync(string userId, string destinationFolder, IFormFile file)
		{
			long currentUsage = 0;
			if (Directory.Exists(destinationFolder))
			{
				string[] files = Directory.GetFiles(destinationFolder);
				foreach (var f in files)
				{
					var info = new FileInfo(f);
					currentUsage += info.Length;
				}
			}
			else
			{
				Directory.CreateDirectory(destinationFolder);
			}
			if (currentUsage + file.Length > MaxFolderSizeBytes)
			{
				await _hubContext.Clients.Group(userId).SendAsync("ReceiveUploadError", "Недостаточно места в хранилище");
				return false;
			}
			string filePath = Path.Combine(destinationFolder, file.FileName);
			using (var fileStream = new FileStream(filePath, FileMode.Create))
			using (var inputStream = file.OpenReadStream())
			{
				byte[] buffer = new byte[81920];
				long totalRead = 0;
				int read;
				while ((read = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					await fileStream.WriteAsync(buffer, 0, read);
					totalRead += read;
					double progress = (double)totalRead / file.Length * 100;
					await _hubContext.Clients.Group(userId).SendAsync("ReceiveUploadProgress", progress);
				}
			}
			await _hubContext.Clients.Group(userId).SendAsync("ReceiveUploadProgress", 100);
			return true;
		}
	}
}
