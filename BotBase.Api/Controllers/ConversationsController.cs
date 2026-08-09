using BotBase.Api.Data;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var businessId = User.GetBusinessId();
        var conversations = await db.Conversations
            .Where(c => c.BusinessId == businessId)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => new { c.Id, c.TelegramChatId, c.StartedAt })
            .ToListAsync();
        return Ok(conversations);
    }
}
