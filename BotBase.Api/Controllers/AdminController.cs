using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BotBase.Api.Controllers;

[Authorize(Roles = AdminAuthService.Role)]
[ApiController]
[Route("api/admin")]
public class AdminController(AppDbContext db, TelegramService telegram, AuthService auth, PlatformSettingsService settings, PasswordService passwords, IConfiguration config) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        return Ok(new
        {
            businesses = await db.Businesses.CountAsync(),
            activeBusinesses = await db.Businesses.CountAsync(b => b.IsActive),
            connectedBots = await db.Businesses.CountAsync(b => b.BotToken != null),
            appointments = await db.Appointments.CountAsync(),
            appointmentsWeek = await db.Appointments.CountAsync(a => a.CreatedAt >= weekAgo),
            messagesWeek = await db.Messages.CountAsync(m => m.CreatedAt >= weekAgo),
            openTickets = await db.SupportTickets.CountAsync(t => t.Status == Data.Entities.SupportTicketStatus.Open)
        });
    }

    [HttpGet("businesses")]
    public async Task<IActionResult> Businesses()
    {
        var list = await db.Businesses
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new AdminBusinessDto(
                b.Id, b.BusinessName, b.Email, b.CreatedAt, b.IsActive,
                b.BotToken != null, b.OwnerNotificationChatId != null,
                db.Procedures.Count(p => p.BusinessId == b.Id && p.IsActive),
                db.KnowledgeChunks.Count(k => k.BusinessId == b.Id),
                db.Appointments.Count(a => a.BusinessId == b.Id),
                b.Conversations.Count,
                db.Messages.Where(m => m.Conversation.BusinessId == b.Id).Max(m => (DateTime?)m.CreatedAt),
                db.SupportTickets.Count(t => t.BusinessId == b.Id && t.Status == Data.Entities.SupportTicketStatus.Open)))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("businesses/{id:guid}/bot")]
    public async Task<IActionResult> BotInfo(Guid id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        if (b.BotToken is null) return Ok(new { connected = false });
        var username = await telegram.GetBotUsernameAsync(b.BotToken);
        var hook = await telegram.GetWebhookInfoAsync(b.BotToken);
        return Ok(new { connected = true, username, tokenValid = username is not null, webhookUrl = hook?.Url, pending = hook?.PendingUpdateCount, lastError = hook?.LastErrorMessage });
    }

    [HttpPost("businesses/{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id) => SetActive(id, false);

    [HttpPost("businesses/{id:guid}/enable")]
    public Task<IActionResult> Enable(Guid id) => SetActive(id, true);

    async Task<IActionResult> SetActive(Guid id, bool active)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        b.IsActive = active;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // «Перезапуск»: проверяем токен и заново регистрируем webhook — лечит бота, который перестал получать сообщения.
    [HttpPost("businesses/{id:guid}/restart-bot")]
    public async Task<IActionResult> RestartBot(Guid id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        if (b.BotToken is null) return BadRequest(new { error = "У клиента не подключён бот" });
        var username = await telegram.GetBotUsernameAsync(b.BotToken);
        if (username is null) return BadRequest(new { error = "Токен бота недействителен: клиенту нужно подключить бота заново" });
        var ok = await telegram.SetWebhookAsync(b.BotToken, $"{config["WebhookBaseUrl"]}/webhook/{b.Id}");
        var hook = await telegram.GetWebhookInfoAsync(b.BotToken);
        return Ok(new { ok, username, webhookUrl = hook?.Url, pending = hook?.PendingUpdateCount, lastError = hook?.LastErrorMessage });
    }

    [HttpPost("businesses/{id:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        var token = auth.GenerateImpersonationJwt(b, User.FindFirstValue(ClaimTypes.Email) ?? "admin");
        return Ok(new { token, businessName = b.BusinessName });
    }

    // Временный пароль для клиента, который не может войти (если почта ещё не настроена).
    [HttpPost("businesses/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id) =>
        await passwords.AdminResetAsync(id) is { } temp ? Ok(new { temporaryPassword = temp }) : NotFound();

    // Удаление необратимо, поэтому требуем повторить название бизнеса.
    [HttpDelete("businesses/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string confirmName)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        if (!string.Equals(confirmName?.Trim(), b.BusinessName.Trim(), StringComparison.Ordinal))
            return BadRequest(new { error = "Название не совпадает — удаление отменено" });
        if (b.BotToken is not null) await telegram.SetWebhookAsync(b.BotToken, "");
        db.Businesses.Remove(b);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(new SettingsDto(
        await settings.GetAsync(PlatformSettingsService.SupportEmail),
        await settings.GetAsync(PlatformSettingsService.SupportTelegramChatId),
        !string.IsNullOrEmpty(config["Smtp:User"]) && !string.IsNullOrEmpty(config["Smtp:Password"]),
        !string.IsNullOrEmpty(config["Support:TelegramBotToken"])));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SettingsDto req)
    {
        if (req.SupportEmail is { Length: > 0 } e && !System.Net.Mail.MailAddress.TryCreate(e.Trim(), out _))
            return BadRequest(new { error = "Некорректный email" });
        if (req.SupportTelegramChatId is { Length: > 0 } c && !long.TryParse(c.Trim(), out _))
            return BadRequest(new { error = "Chat ID должен быть числом" });
        await settings.SetAsync(PlatformSettingsService.SupportEmail, req.SupportEmail);
        await settings.SetAsync(PlatformSettingsService.SupportTelegramChatId, req.SupportTelegramChatId);
        return NoContent();
    }

    public record AdminBusinessDto(Guid Id, string BusinessName, string Email, DateTime CreatedAt, bool IsActive,
        bool HasBot, bool OwnerConnected, int Procedures, int KnowledgeFiles, int Appointments, int Conversations,
        DateTime? LastMessageAt, int OpenTickets);
    public record SettingsDto(string? SupportEmail, string? SupportTelegramChatId, bool SmtpConfigured = false, bool TelegramConfigured = false);
}
