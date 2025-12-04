using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace VCS_DOCs.Utilities;

public sealed class CustomUserIdProvider : IUserIdProvider
{
	public string? GetUserId(HubConnectionContext connection) =>
		connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
