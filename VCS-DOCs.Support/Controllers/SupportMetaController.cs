// VCS_DOCs.Support.Controllers/SupportMetaController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("api/support")]
    public sealed class SupportMetaController : ControllerBase
    {
        // Доступен всем, кто может войти в Service Desk (админы, агенты, пользователи)
        [Authorize(Policy = "SupportDeskAccess")]
        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new { ok = true, ts = DateTimeOffset.UtcNow });
    }
}
