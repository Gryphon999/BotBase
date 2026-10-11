using BotBase.Api.Data.Entities;
using BotBase.Api.Filters;
using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

// Настройки автосообщений клиентам: напоминания, просьба об отзыве, «давно не были», часовой пояс.
[Authorize]
[RequiresCrm]
[ApiController]
[Route("api/automation")]
public class AutomationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var b = await db.Businesses.FindAsync(User.GetBusinessId());
        if (b is null) return NotFound();
        return Ok(new AutomationSettings(b.TimeZone, b.RemindersEnabled, b.ReviewRequestsEnabled, b.ReviewUrl, b.WinbackEnabled, b.WinbackAfterDays, b.MessagesLanguage));
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
        b.MessagesLanguage = ClientTexts.Normalize(req.MessagesLanguage);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Образцы сообщений на выбранном языке — для превью на странице «Автосообщения».
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] string? lang)
    {
        var b = await db.Businesses.FindAsync(User.GetBusinessId());
        if (b is null) return NotFound();
        var l = ClientTexts.Normalize(lang);
        var now = BusinessClock.Now(b.TimeZone);
        var sample = new Appointment { Id = Guid.Empty, ClientName = l switch { "hy" => "Անի", "en" => "Anna", _ => "Анна" }, ProcedureName = l switch { "hy" => "Մանիկյուր", "en" => "Manicure", _ => "Маникюр" }, ScheduledAt = now.Date.AddDays(1).AddHours(14) };
        var client = new Client { Name = sample.ClientName };
        return Ok(new
        {
            reminder = ClientTexts.Reminder(l, b, sample, now),
            reminderButtons = ClientTexts.ReminderButtons(l, sample)[0].Select(x => x.Text),
            review = ClientTexts.ReviewRequest(l, b),
            winback = ClientTexts.Winback(l, b, client)
        });
    }

    public record AutomationSettings(string TimeZone, bool RemindersEnabled, bool ReviewRequestsEnabled, string? ReviewUrl, bool WinbackEnabled, int WinbackAfterDays, string MessagesLanguage = "ru");
}
