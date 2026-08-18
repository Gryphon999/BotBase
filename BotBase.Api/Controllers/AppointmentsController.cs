using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController(AppDbContext db, CrmWebhookService webhook) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? date, [FromQuery] DateTime? updatedSince)
    {
        var businessId = User.GetBusinessId();
        var query = db.Appointments.Where(a => a.BusinessId == businessId);

        if (date.HasValue)
        {
            var day = date.Value.Date;
            query = query.Where(a => a.ScheduledAt.Date == day);
        }

        if (updatedSince.HasValue)
        {
            var since = updatedSince.Value.Kind == DateTimeKind.Utc
                ? updatedSince.Value
                : updatedSince.Value.ToUniversalTime();
            query = query.Where(a => a.UpdatedAt >= since);
        }

        var list = await query
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new AppointmentDto(
                a.Id, a.ProcedureName, a.ClientName, a.ClientPhone,
                a.ScheduledAt, a.DurationMinutes, a.Status.ToString(),
                a.CreatedAt, a.UpdatedAt))
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest req)
    {
        var businessId = User.GetBusinessId();
        var appt = new Appointment
        {
            Id              = Guid.NewGuid(),
            BusinessId      = businessId,
            ProcedureName   = req.ProcedureName,
            ClientName      = req.ClientName,
            ClientPhone     = req.ClientPhone,
            ScheduledAt     = req.ScheduledAt.ToUniversalTime(),
            DurationMinutes = req.DurationMinutes,
            Status          = AppointmentStatus.Pending
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        await FireWebhookAsync(businessId, "appointment.created", appt);

        return Ok(new AppointmentDto(
            appt.Id, appt.ProcedureName, appt.ClientName, appt.ClientPhone,
            appt.ScheduledAt, appt.DurationMinutes, appt.Status.ToString(),
            appt.CreatedAt, appt.UpdatedAt));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest req)
    {
        var businessId = User.GetBusinessId();
        var appt = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (appt is null) return NotFound();

        if (!Enum.TryParse<AppointmentStatus>(req.Status, out var status))
            return BadRequest(new { error = "Неверный статус. Допустимые: Pending, Confirmed, Cancelled" });

        appt.Status    = status;
        appt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var eventName = status switch
        {
            AppointmentStatus.Confirmed => "appointment.confirmed",
            AppointmentStatus.Cancelled => "appointment.cancelled",
            _                           => "appointment.updated"
        };
        await FireWebhookAsync(businessId, eventName, appt);

        return NoContent();
    }

    private async Task FireWebhookAsync(Guid businessId, string eventName, Appointment appt)
    {
        var business = await db.Businesses.FindAsync(businessId);
        if (business?.CrmWebhookUrl is { } url)
            await webhook.FireAsync(url, eventName, appt);
    }

    public record AppointmentDto(
        Guid Id, string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes, string Status,
        DateTime CreatedAt, DateTime UpdatedAt);

    public record CreateAppointmentRequest(
        string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes);

    public record UpdateStatusRequest(string Status);
}
