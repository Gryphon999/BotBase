using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[Authorize(Roles = AdminAuthService.Role)]
[ApiController]
[Route("api/admin/support")]
public class AdminSupportController(AppDbContext db, NotificationService notify) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        var q = db.SupportTickets.AsQueryable();
        if (Enum.TryParse<SupportTicketStatus>(status, true, out var s)) q = q.Where(t => t.Status == s);
        var list = await q.OrderBy(t => t.Status).ThenByDescending(t => t.UpdatedAt)
            .Select(t => new AdminTicketDto(t.Id, t.Subject, t.Status.ToString(), t.UpdatedAt, t.BusinessId,
                t.Business.BusinessName, t.Business.Email, t.Messages.Count,
                t.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Text).FirstOrDefault()))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("tickets/{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var t = await Load(id);
        return t is null ? NotFound() : Ok(new { ticket = SupportController.ToDetail(t), businessName = t.Business.BusinessName, businessEmail = t.Business.Email });
    }

    [HttpPost("tickets/{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, SupportController.ReplyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest(new { error = "Пустое сообщение" });
        var t = await Load(id);
        if (t is null) return NotFound();
        var msg = new SupportMessage { Id = Guid.NewGuid(), TicketId = t.Id, FromAdmin = true, Text = req.Text.Trim() };
        db.SupportMessages.Add(msg); // явный Add: иначе EF примет сообщение с заданным Id за существующее и сделает UPDATE
        t.Messages.Add(msg);
        t.Status = SupportTicketStatus.Answered;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await notify.SendEmailAsync(t.Business.Email, $"BotBase: ответ поддержки — {t.Subject}",
            $"{req.Text.Trim()}\n\nОтветить можно в личном кабинете, раздел «Поддержка».");
        return Ok(SupportController.ToDetail(t));
    }

    [HttpPost("tickets/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id)
    {
        var t = await db.SupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        t.Status = SupportTicketStatus.Closed;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    Task<SupportTicket?> Load(Guid id) =>
        db.SupportTickets.Include(t => t.Messages).Include(t => t.Business).FirstOrDefaultAsync(t => t.Id == id);

    public record AdminTicketDto(Guid Id, string Subject, string Status, DateTime UpdatedAt, Guid BusinessId,
        string BusinessName, string BusinessEmail, int MessageCount, string? LastMessage);
}
