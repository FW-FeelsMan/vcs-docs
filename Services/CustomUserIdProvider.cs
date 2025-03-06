using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace VCS_DOCs.Services
{
	public class CustomUserIdProvider : IUserIdProvider
	{
		public string GetUserId(HubConnectionContext connection)
		{
			return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		}
	}
}
