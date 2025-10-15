// Support/Controllers/OpsAccountsController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/accounts")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsAccountsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public OpsAccountsController(ApplicationDbContext db)
    {
        _db = db;
    }
    
    [HttpGet("me")]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("SupportAdmin");
        var isAgent = User.IsInRole("SupportAgent") || isAdmin;
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
        return Ok(new { id, isAdmin, isAgent, roles });
    }

    [HttpGet("agents")]
    public async Task<IActionResult> Agents()
    {
        var items = await (
            from u in _db.Users
            join ur in _db.UserRoles on u.Id equals ur.UserId
            join r in _db.Roles on ur.RoleId equals r.Id
            where r.Name == "SupportAgent" || r.Name == "SupportAdmin"
            select new
            {
                id = u.Id,
                login = u.UserName,           
                name = u.UserName              
            }
        )
        .OrderBy(x => x.login)
        .ToListAsync();

        return Ok(new { items });
    }
}