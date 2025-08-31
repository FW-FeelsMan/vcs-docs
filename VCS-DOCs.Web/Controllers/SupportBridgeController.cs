using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Web.Controllers
{
    /// <summary>
    /// Мост для внешнего "Саппорта".
    /// Защита: заголовок X-Support-ApiKey должен совпадать c SupportBridge:ApiKey в конфиге V-DOCs.
    /// </summary>
    [ApiController]
    [Route("api/_support")]
    public class SupportBridgeController : ControllerBase
    {
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
            if (string.IsNullOrEmpty(expected)) return false;
            if (!Request.Headers.TryGetValue("X-Support-ApiKey", out var given)) return false;
            return string.Equals(given.ToString(), expected, StringComparison.Ordinal);
        }

        // GET /api/_support/presence?ids=id1,id2,id3
        [HttpGet("presence")]
        public IActionResult Presence([FromQuery] string? ids)
        {
            if (!CheckKey()) return Unauthorized("Invalid key.");

            var list = (ids ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var result = list.ToDictionary(
                id => id,
                id => new
                {
                    online = UserStatusHub.IsOnlineUser(id),
                    lastSeen = (string?)null
                },
                StringComparer.Ordinal
            );

            return Ok(result);
        }

        public sealed class KickDto
        {
            public string? UserId
            {
                get; set;
            }
        }

        // POST /api/_support/kick  { "userId": "..." }
        [HttpPost("kick")]
        public async Task<IActionResult> Kick([FromBody] KickDto body)
        {
            if (!CheckKey()) return Unauthorized("Invalid key.");
            if (string.IsNullOrWhiteSpace(body?.UserId))
                return BadRequest("userId required.");

            // 1) мгновенный сигнал клиенту
            await _hub.Clients.User(body.UserId).SendAsync("ForceLogout");
            // 2) инвалидируем sid/JwtId
            await _userService.ClearUserJwtIdAsync(body.UserId);

            return Ok(new { ok = true });
        }
    }
}
