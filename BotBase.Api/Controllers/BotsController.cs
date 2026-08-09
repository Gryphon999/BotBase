using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Models.Bots;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/bots")]
public class BotsController(AppDbContext db, TelegramService telegram, IConfiguration config) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Setup(SetupBotRequest req)
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return NotFound();

        var username = await telegram.GetBotUsernameAsync(req.BotToken);
        if (username is null)
            return BadRequest(new { error = "Неверный токен бота" });

        var webhookUrl = $"{config["WebhookBaseUrl"]}/webhook/{businessId}";
        await telegram.SetWebhookAsync(req.BotToken, webhookUrl);

        business.BotToken = req.BotToken;
        await db.SaveChangesAsync();

        return Ok(new BotStatusResponse(true, username, business.IsActive));
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return NotFound();

        if (business.BotToken is null)
            return Ok(new BotStatusResponse(false, null, false));

        var username = await telegram.GetBotUsernameAsync(business.BotToken);
        return Ok(new BotStatusResponse(true, username, business.IsActive));
    }
}
