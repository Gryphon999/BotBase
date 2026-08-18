using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? date)
    {
        var businessId = User.GetBusinessId();
        var query = db.Appointments.Where(a => a.BusinessId == businessId);

        if (date.HasValue)
        {
            var day = date.Value.Date;
            query = query.Where(a => a.ScheduledAt.Date == day);
        }

        var list = await query
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new AppointmentDto(
                a.Id, a.ProcedureName, a.ClientName, a.ClientPhone,
                a.ScheduledAt, a.DurationMinutes, a.Status.ToString()))
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest req)
    {
        var appt = new Appointment
        {
            Id              = Guid.NewGuid(),
            BusinessId      = User.GetBusinessId(),
            ProcedureName   = req.ProcedureName,
            ClientName      = req.ClientName,
            ClientPhone     = req.ClientPhone,
            ScheduledAt     = req.ScheduledAt.ToUniversalTime(),
            DurationMinutes = req.DurationMinutes,
            Status          = AppointmentStatus.Pending
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        return Ok(new AppointmentDto(
            appt.Id, appt.ProcedureName, appt.ClientName, appt.ClientPhone,
            appt.ScheduledAt, appt.DurationMinutes, appt.Status.ToString()));
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

        appt.Status = status;
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record AppointmentDto(
        Guid Id, string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes, string Status);

    public record CreateAppointmentRequest(
        string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes);

    public record UpdateStatusRequest(string Status);
}
