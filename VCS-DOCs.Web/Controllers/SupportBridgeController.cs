using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Web.Controllers;

[ApiController]
[Route("api/_support")]
public sealed class SupportBridgeController : ControllerBase
{
	private const string ApiKeyHeader = "X-Support-ApiKey";

	private readonly IConfiguration _cfg;
	private readonly IHubContext<UserStatusHub> _hub;
	private readonly IUserService _userService;

	public SupportBridgeController(
		IConfiguration cfg,
		IHubContext<UserStatusHub> hub,
		IUserService userService)
	{
		_cfg = cfg;
		_hub = hub;
		_userService = userService;
	}

	private bool CheckKey()
	{
		var expected = _cfg["SupportBridge:ApiKey"];
		if (string.IsNullOrWhiteSpace(expected))
			return false;

		if (!Request.Headers.TryGetValue(ApiKeyHeader, out var given))
			return false;

		return string.Equals(given.ToString(), expected, StringComparison.Ordinal);
	}

	[HttpGet("presence")]
	public IActionResult Presence([FromQuery] string? ids)
	{
		if (!CheckKey())
			return Unauthorized("Invalid key.");

		var list = (ids ?? string.Empty)
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		var result = list.ToDictionary(
			id => id,
			id => new { online = UserStatusHub.IsOnlineUser(id), lastSeen = (string?)null },
			StringComparer.Ordinal);

		return Ok(result);
	}

	public sealed record KickDto(string? UserId);

	[HttpPost("kick")]
	public async Task<IActionResult> Kick([FromBody] KickDto body, CancellationToken ct)
	{
		if (!CheckKey())
			return Unauthorized("Invalid key.");

		if (string.IsNullOrWhiteSpace(body.UserId))
			return BadRequest("userId required.");

		await _hub.Clients.User(body.UserId).SendAsync("ForceLogout", cancellationToken: ct);
		await _userService.ClearUserJwtIdAsync(body.UserId);

		return Ok(new { ok = true });
	}
}