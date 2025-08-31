using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace VCS_DOCs.Utilities
{
	public class CustomUserIdProvider : IUserIdProvider
	{
        public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
