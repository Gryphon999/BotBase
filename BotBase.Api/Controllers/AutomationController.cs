using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

// Настройки автосообщений клиентам: напоминания, просьба об отзыве, «давно не были», часовой пояс.
[Authorize]
[ApiController]
[Route("api/automation")]
public class AutomationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var b = await db.Businesses.FindAsync(User.GetBusinessId());
        if (b is null) return NotFound();
        return Ok(new AutomationSettings(b.TimeZone, b.RemindersEnabled, b.ReviewRequestsEnabled, b.ReviewUrl, b.WinbackEnabled, b.WinbackAfterDays));
    }

    [HttpPut]
    public async Task<IActionResult> Update(AutomationSettings req)
    {
        var b = await db.Businesses.FindAsync(User.GetBusinessId());
        if (b is null) return NotFound();
        if (!BusinessClock.IsValidZone(req.TimeZone)) return BadRequest(new { error = "Неизвестный часовой пояс." });
        if (req.WinbackAfterDays is < 14 or > 365) return BadRequest(new { error = "«Давно не были»: от 14 до 365 дней." });
        var url = string.IsNullOrWhiteSpace(req.ReviewUrl) ? null : req.ReviewUrl.Trim();
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")))
            return BadRequest(new { error = "Ссылка для отзывов должна начинаться с https://" });

        b.TimeZone = req.TimeZone;
        b.RemindersEnabled = req.RemindersEnabled;
        b.ReviewRequestsEnabled = req.ReviewRequestsEnabled;
        b.ReviewUrl = url;
        b.WinbackEnabled = req.WinbackEnabled;
        b.WinbackAfterDays = req.WinbackAfterDays;
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record AutomationSettings(string TimeZone, bool RemindersEnabled, bool ReviewRequestsEnabled, string? ReviewUrl, bool WinbackEnabled, int WinbackAfterDays);
}
