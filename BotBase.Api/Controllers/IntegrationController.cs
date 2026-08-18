using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/integration")]
[Authorize]
public class IntegrationController(AppDbContext db, AuthService auth) : ControllerBase
{
    /// <summary>
    /// Возвращает долгоживущий API-ключ (JWT, 1 год).
    /// CosmetologyCRM хранит его и передаёт в Authorization: Bearer {key}.
    /// </summary>
    [HttpGet("apikey")]
    public async Task<IActionResult> GetApiKey()
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return Unauthorized();
        var key = auth.GenerateApiKey(business);
        return Ok(new { apiKey = key });
    }

    /// <summary>
    /// Устанавливает URL вебхука, на который BotBase будет POST'ить события записей.
    /// Payload: { "event": "appointment.created|confirmed|cancelled", "appointment": {...} }
    /// </summary>
    [HttpPut("webhook")]
    public async Task<IActionResult> SetWebhook([FromBody] SetWebhookRequest req)
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return Unauthorized();

        business.CrmWebhookUrl = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim();
        await db.SaveChangesAsync();
        return Ok(new { webhookUrl = business.CrmWebhookUrl });
    }

    [HttpGet("webhook")]
    public async Task<IActionResult> GetWebhook()
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return Unauthorized();
        return Ok(new { webhookUrl = business.CrmWebhookUrl });
    }

    public record SetWebhookRequest(string? Url);
}
