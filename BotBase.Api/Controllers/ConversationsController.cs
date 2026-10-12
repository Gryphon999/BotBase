using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController(AppDbContext db, ClientDataService clientData) : ControllerBase
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

    [HttpGet("{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id)
    {
        var businessId = User.GetBusinessId();
        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId);
        if (conv is null) return NotFound();

        var messages = await db.Messages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content, m.CreatedAt })
            .ToListAsync();
        return Ok(messages);
    }

    // Удаление данных клиента из «Разговоров» — доступно на любом тарифе (CRM-раздел «Клиенты» есть не у всех).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        ClientsController.ToResponse(await clientData.EraseConversationAsync(User.GetBusinessId(), id));
}
