using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/self")]
[Authorize(Policy = "SupportDeskAccess")]
public sealed class SelfTicketsController : ControllerBase
{
    // DTO под фронт (минимум, что нужно)
    public sealed record UserOpenTicketDto(
        string Id,
        string Subject,
        string Wait,        // "user" | "operator"
        DateTime CreatedAt,
        DateTime UpdatedAt,
        bool Notify);

    public sealed record UserClosedTicketDto(
        string Id,
        string Subject,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    // Пока мок — позже заменим на реальную выборку из БД
    [HttpGet("open")]
    public IActionResult Open()
    {
        var now = DateTime.UtcNow;
        var list = new[]
        {
            new UserOpenTicketDto("121000ab", "Проблема с входом", "operator", now.AddDays(-1), now.AddHours(-1), false),
            new UserOpenTicketDto("121001ab", "Не приходит письмо", "user",     now.AddHours(-2), now.AddMinutes(-70), true ),
            new UserOpenTicketDto("121002ab", "Доступ к отчётам",   "operator", now.AddHours(-3), now.AddMinutes(-30), false),
        };
        return Ok(list);
    }

    [HttpGet("closed")]
    public IActionResult Closed()
    {
        var now = DateTime.UtcNow;
        var list = new[]
        {
            new UserClosedTicketDto("221000zx", "Демо: закрытая заявка №1", now.AddDays(-3), now.AddDays(-2)),
            new UserClosedTicketDto("221001zx", "Демо: закрытая заявка №2", now.AddDays(-5), now.AddDays(-4)),
        };
        return Ok(list);
    }
}
