using BotBase.Api.Data;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

// Сводка для главной страницы кабинета: цифры и чек-лист «Начало работы».
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var id = User.GetBusinessId();
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        return Ok(new
        {
            businessName = b.BusinessName,
            hasBot = b.BotToken != null,
            ownerConnected = b.OwnerNotificationChatId != null,
            procedures = await db.Procedures.CountAsync(p => p.BusinessId == id && p.IsActive),
            knowledgeFiles = await db.KnowledgeChunks.CountAsync(k => k.BusinessId == id),
            scheduleSet = await db.WorkSchedules.AnyAsync(s => s.BusinessId == id && s.MasterId == null && s.IsWorkingDay),
            upcomingAppointments = await db.Appointments.CountAsync(a => a.BusinessId == id && a.ScheduledAt >= now && a.Status != Data.Entities.AppointmentStatus.Cancelled),
            appointmentsWeek = await db.Appointments.CountAsync(a => a.BusinessId == id && a.CreatedAt >= weekAgo),
            conversationsWeek = await db.Conversations.CountAsync(c => c.BusinessId == id && c.Messages.Any(m => m.CreatedAt >= weekAgo)),
            messagesWeek = await db.Messages.CountAsync(m => m.Conversation.BusinessId == id && m.CreatedAt >= weekAgo)
        });
    }
}
