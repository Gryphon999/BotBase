using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

// Внимание: GET / POST / PATCH status используются CRM-интеграцией (CosmetologyCRM) —
// существующие поля и поведение не менять, только добавлять новые.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentsController(AppDbContext db, CrmWebhookService webhook, BookingService booking, ClientMessenger messenger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? date,
        [FromQuery] DateTime? updatedSince,
        [FromQuery] string? month,
        [FromQuery] Guid? clientId)
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

        if (month is not null && DateTime.TryParseExact(month, "yyyy-MM",
                null, System.Globalization.DateTimeStyles.None, out var monthStart))
        {
            monthStart = DateTime.SpecifyKind(monthStart, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            query = query.Where(a => a.ScheduledAt >= monthStart && a.ScheduledAt < monthEnd);
        }

        if (clientId.HasValue)
            query = query.Where(a => a.ClientId == clientId);

        var list = await query
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new AppointmentDto(
                a.Id, a.ProcedureName, a.ClientName, a.ClientPhone,
                a.ScheduledAt, a.DurationMinutes, a.Status.ToString(),
                a.CreatedAt, a.UpdatedAt,
                a.MasterId, a.Master != null ? a.Master.Name : null, a.ClientId, a.Source.ToString(), a.Price,
                a.ReminderSentAt, a.ClientConfirmedAt, a.NoShow, a.Rating,
                (a.Client != null && a.Client.TelegramChatId != null) || a.ConversationId != null))
            .ToListAsync();

        return Ok(list);
    }

    // Создание записи внешней системой (CRM-интеграция). Поведение прежнее: без проверки расписания.
    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest req)
    {
        var businessId = User.GetBusinessId();
        var client = await booking.UpsertClientAsync(businessId, req.ClientName, req.ClientPhone, null);
        var appt = new Appointment
        {
            Id              = Guid.NewGuid(),
            BusinessId      = businessId,
            ClientId        = client.Id,
            ProcedureName   = req.ProcedureName,
            ClientName      = req.ClientName,
            ClientPhone     = req.ClientPhone,
            ScheduledAt     = req.ScheduledAt.ToUniversalTime(),
            DurationMinutes = req.DurationMinutes,
            Source          = AppointmentSource.Integration,
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

    // Запись, которую владелец вносит сам (клиент позвонил или пришёл). Проверяется так же, как запись от бота.
    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual(ManualAppointmentRequest req)
    {
        var businessId = User.GetBusinessId();
        var result = await booking.BookAsync(businessId, new BookingRequest(
            "", req.ClientName, req.ClientPhone, req.ScheduledAt, AppointmentSource.Manual,
            DurationMinutes: req.DurationMinutes, ProcedureId: req.ProcedureId, MasterId: req.MasterId,
            IgnoreWorkingHours: req.IgnoreWorkingHours));
        if (result.Appointment is not { } appt)
            return Conflict(new { error = ForOwner(result.Error), freeTimes = result.FreeTimes });

        await FireWebhookAsync(businessId, "appointment.created", appt);
        if (req.NotifyClient)
        {
            var business = await db.Businesses.FindAsync(businessId);
            await messenger.SendAsync(business!, appt, ClientTexts.Booked(business!, appt));
        }
        return Ok(new { appt.Id, appt.ScheduledAt, masterName = appt.Master?.Name });
    }

    [HttpPut("{id:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid id, RescheduleRequest req)
    {
        var businessId = User.GetBusinessId();
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (appt is null) return NotFound();
        if (appt.Status == AppointmentStatus.Cancelled) return BadRequest(new { error = "Отменённую запись нельзя перенести — создайте новую." });

        var result = await booking.RescheduleAsync(appt, req.ScheduledAt, req.MasterId, req.IgnoreWorkingHours);
        if (result.Appointment is null)
            return Conflict(new { error = ForOwner(result.Error), freeTimes = result.FreeTimes });

        await FireWebhookAsync(businessId, "appointment.updated", appt);
        if (req.NotifyClient)
        {
            var business = await db.Businesses.FindAsync(businessId);
            await messenger.SendAsync(business!, appt, ClientTexts.Rescheduled(appt));
        }
        return NoContent();
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

        var wasCancelled = appt.Status == AppointmentStatus.Cancelled;
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

        // Владелец отменил запись в кабинете — клиент узнаёт об этом сразу, а не придя к закрытой двери.
        // CRM-интеграция поле NotifyClient не передаёт — для неё всё как раньше.
        if (status == AppointmentStatus.Cancelled && !wasCancelled && req.NotifyClient == true)
        {
            var business = await db.Businesses.FindAsync(businessId);
            await db.Entry(appt).Reference(a => a.Master).LoadAsync();
            await messenger.SendAsync(business!, appt, ClientTexts.CancelledByOwner(appt));
        }

        return NoContent();
    }

    [HttpPatch("{id:guid}/no-show")]
    public async Task<IActionResult> SetNoShow(Guid id, [FromBody] NoShowRequest req)
    {
        var businessId = User.GetBusinessId();
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (appt is null) return NotFound();
        appt.NoShow = req.NoShow;
        appt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Тексты отказа написаны для клиента бота; владельцу в кабинете вопрос «Какое вам подходит?» не нужен.
    static string? ForOwner(string? error) => error?.Replace(" Какое вам подходит?", "").Replace(" Подскажите, пожалуйста, другой день.", "").Replace(" Выберите, пожалуйста, время в эти часы.", "")
        .Replace(" Подскажите, пожалуйста, другую дату или время.", "");

    private async Task FireWebhookAsync(Guid businessId, string eventName, Appointment appt)
    {
        var business = await db.Businesses.FindAsync(businessId);
        if (business?.CrmWebhookUrl is { } url)
            await webhook.FireAsync(url, eventName, appt);
    }

    public record AppointmentDto(
        Guid Id, string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes, string Status,
        DateTime CreatedAt, DateTime UpdatedAt,
        Guid? MasterId = null, string? MasterName = null, Guid? ClientId = null, string Source = "Bot", decimal? Price = null,
        DateTime? ReminderSentAt = null, DateTime? ClientConfirmedAt = null, bool NoShow = false, int? Rating = null,
        bool HasTelegram = false);

    public record CreateAppointmentRequest(
        string ProcedureName, string ClientName, string ClientPhone,
        DateTime ScheduledAt, int DurationMinutes);

    public record ManualAppointmentRequest(
        string ClientName, string ClientPhone, Guid ProcedureId, DateTime ScheduledAt,
        Guid? MasterId = null, int? DurationMinutes = null, bool IgnoreWorkingHours = false, bool NotifyClient = true);

    public record RescheduleRequest(DateTime ScheduledAt, Guid? MasterId = null, bool IgnoreWorkingHours = false, bool NotifyClient = true);

    public record UpdateStatusRequest(string Status, bool? NotifyClient = null);

    public record NoShowRequest(bool NoShow);
}
