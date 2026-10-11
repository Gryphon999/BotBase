using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/support")]
public class SupportController(AppDbContext db, NotificationService notify) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> List()
    {
        var businessId = User.GetBusinessId();
        var tickets = await db.SupportTickets
            .Where(t => t.BusinessId == businessId)
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new TicketDto(t.Id, t.Subject, t.Status.ToString(), t.CreatedAt, t.UpdatedAt,
                t.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Text).FirstOrDefault()))
            .ToListAsync();
        return Ok(tickets);
    }

    [HttpGet("tickets/{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var ticket = await Load(id);
        return ticket is null ? NotFound() : Ok(ToDetail(ticket));
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> Create(CreateTicketRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "Заполните тему и сообщение" });
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return NotFound();

        var ticket = new SupportTicket { Id = Guid.NewGuid(), BusinessId = businessId, Subject = req.Subject.Trim() };
        ticket.Messages.Add(new SupportMessage { Id = Guid.NewGuid(), Text = req.Text.Trim() });
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        await notify.NotifySupportAsync($"Новое обращение: {ticket.Subject}",
            $"{business.BusinessName} ({business.Email})\n\n{req.Text.Trim()}");
        return Ok(ToDetail(ticket));
    }

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> Reply(Guid id, ReplyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest(new { error = "Пустое сообщение" });
        var ticket = await Load(id);
        if (ticket is null) return NotFound();

        var msg = new SupportMessage { Id = Guid.NewGuid(), TicketId = ticket.Id, Text = req.Text.Trim() };
        db.SupportMessages.Add(msg); // явный Add: иначе EF примет сообщение с заданным Id за существующее и сделает UPDATE
        ticket.Messages.Add(msg);
        ticket.Status = SupportTicketStatus.Open;
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await notify.NotifySupportAsync($"Ответ клиента: {ticket.Subject}",
            $"{ticket.Business.BusinessName} ({ticket.Business.Email})\n\n{req.Text.Trim()}");
        return Ok(ToDetail(ticket));
    }

    Task<SupportTicket?> Load(Guid id)
    {
        var businessId = User.GetBusinessId();
        return db.SupportTickets.Include(t => t.Messages).Include(t => t.Business)
            .FirstOrDefaultAsync(t => t.Id == id && t.BusinessId == businessId);
    }

    internal static TicketDetailDto ToDetail(SupportTicket t) => new(t.Id, t.Subject, t.Status.ToString(), t.CreatedAt,
        t.Messages.OrderBy(m => m.CreatedAt).Select(m => new SupportMessageDto(m.FromAdmin, m.Text, m.CreatedAt)).ToList());

    public record CreateTicketRequest(string Subject, string Text);
    public record ReplyRequest(string Text);
    public record TicketDto(Guid Id, string Subject, string Status, DateTime CreatedAt, DateTime UpdatedAt, string? LastMessage);
    public record TicketDetailDto(Guid Id, string Subject, string Status, DateTime CreatedAt, List<SupportMessageDto> Messages);
    public record SupportMessageDto(bool FromAdmin, string Text, DateTime CreatedAt);
}
