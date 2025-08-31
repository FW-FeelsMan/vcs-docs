using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using System;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/projects")]
[Authorize(Roles = Roles.SupportAdmin)]
public class SupportProjectsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public SupportProjectsController(ApplicationDbContext db) => _db = db;

    // GET: /api/support/projects
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.SupportProjects.AsNoTracking()
            .OrderBy(p => p.DisplayName)
            .ToListAsync();

        return Ok(items.Select(p => new
        {
            id = p.Id,
            code = p.AppCode,
            name = p.DisplayName,
            enabled = p.IsEnabled,
            capabilities = (long)p.Capabilities,
            baseUrl = p.BaseUrl,
            // apiKey/metadata наружу не отдаём
        }));
    }

    public sealed class CreateDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public long Capabilities { get; set; } = (long)(ProjectCapability.PresenceRead | ProjectCapability.Kick);
        public string? BaseUrl
        {
            get; set;
        }
        public string? ApiKey
        {
            get; set;
        }

        // Придут из твоего модала как лишние — биндер их просто проигнорит
        public string? Type
        {
            get; set;
        }
        public string? HubUrl
        {
            get; set;
        }
    }

    // POST: /api/support/projects
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Code and Name are required.");

        var code = dto.Code.Trim();
        var exists = await _db.SupportProjects
            .AnyAsync(p => p.AppCode == code);
        if (exists) return Conflict("Project with this code already exists.");

        var p = new SupportProject
        {
            AppCode = code,
            DisplayName = dto.Name.Trim(),
            IsEnabled = dto.Enabled,
            Capabilities = (ProjectCapability)dto.Capabilities,
            BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl!.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(dto.ApiKey) ? null : dto.ApiKey!.Trim(),
            CreatedUtc = DateTime.UtcNow
        };

        _db.SupportProjects.Add(p);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, id = p.Id });
    }

    public sealed class UpdateDto
    {
        public string? Name
        {
            get; set;
        }
        public bool? Enabled
        {
            get; set;
        }
        public long? Capabilities
        {
            get; set;
        }
        public string? BaseUrl
        {
            get; set;
        }
        public string? ApiKey
        {
            get; set;
        }
    }

    // PUT: /api/support/projects/{id}
    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateDto dto)
    {
        var p = await _db.SupportProjects.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name)) p.DisplayName = dto.Name.Trim();
        if (dto.Enabled.HasValue) p.IsEnabled = dto.Enabled.Value;
        if (dto.Capabilities.HasValue) p.Capabilities = (ProjectCapability)dto.Capabilities.Value;
        if (dto.BaseUrl != null) p.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
        if (dto.ApiKey != null) p.ApiKey = string.IsNullOrWhiteSpace(dto.ApiKey) ? null : dto.ApiKey.Trim();

        p.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // DELETE: /api/support/projects/{id}
    [HttpDelete("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var p = await _db.SupportProjects.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        _db.SupportProjects.Remove(p);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
