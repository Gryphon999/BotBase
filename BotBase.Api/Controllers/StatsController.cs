using BotBase.Api.Filters;
using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

// Статистика за месяц: сколько записей, сколько из них сделал бот, выручка, неявки, популярные услуги, загрузка мастеров.
[Authorize]
[RequiresCrm]
[ApiController]
[Route("api/stats")]
public class StatsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? month)
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        var now = BusinessClock.Now(business!.TimeZone);
        var start = month is not null && DateTime.TryParseExact(month, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var m)
            ? m : new DateTime(now.Year, now.Month, 1);
        start = BusinessClock.AsStored(start);
        var end = start.AddMonths(1);

        var appts = await db.Appointments
            .Where(a => a.BusinessId == businessId && a.ScheduledAt >= start && a.ScheduledAt < end)
            .Select(a => new { a.ScheduledAt, a.Status, a.Source, a.Price, a.NoShow, a.ClientId, a.ProcedureName, a.ClientConfirmedAt, a.ReminderSentAt, a.Rating, Master = a.Master != null ? a.Master.Name : null })
            .ToListAsync();

        var active = appts.Where(a => a.Status != AppointmentStatus.Cancelled).ToList();
        var visits = active.Where(a => a.ScheduledAt < now && !a.NoShow).ToList();

        // Новый клиент — тот, у кого первый визит (не отменённый) пришёлся на этот месяц.
        var clientIds = active.Where(a => a.ClientId != null).Select(a => a.ClientId!.Value).Distinct().ToList();
        var firstVisits = await db.Appointments
            .Where(a => a.ClientId != null && clientIds.Contains(a.ClientId.Value) && a.Status != AppointmentStatus.Cancelled)
            .GroupBy(a => a.ClientId)
            .Select(g => g.Min(a => a.ScheduledAt))
            .ToListAsync();

        var ratings = appts.Where(a => a.Rating != null).Select(a => a.Rating!.Value).ToList();
        var reminded = appts.Count(a => a.ReminderSentAt != null && a.ClientConfirmedAt != null);

        return Ok(new
        {
            month = start.ToString("yyyy-MM"),
            total = active.Count,
            byBot = active.Count(a => a.Source == AppointmentSource.Bot),
            byManual = active.Count(a => a.Source == AppointmentSource.Manual),
            byIntegration = active.Count(a => a.Source == AppointmentSource.Integration),
            cancelled = appts.Count(a => a.Status == AppointmentStatus.Cancelled),
            noShows = active.Count(a => a.NoShow),
            upcoming = active.Count(a => a.ScheduledAt >= now),
            visits = visits.Count,
            revenue = visits.Sum(a => a.Price ?? 0),
            botRevenue = visits.Where(a => a.Source == AppointmentSource.Bot).Sum(a => a.Price ?? 0),
            averageCheck = visits.Count(a => a.Price != null) == 0 ? 0 : Math.Round(visits.Where(a => a.Price != null).Average(a => a.Price!.Value)),
            clients = clientIds.Count,
            newClients = firstVisits.Count(d => d >= start && d < end),
            confirmedByReminder = reminded,
            averageRating = ratings.Count == 0 ? (double?)null : Math.Round(ratings.Average(), 1),
            ratingsCount = ratings.Count,
            topProcedures = active.GroupBy(a => a.ProcedureName.Trim())
                .Select(g => new { name = g.Key, count = g.Count(), revenue = g.Where(a => a.ScheduledAt < now && !a.NoShow).Sum(a => a.Price ?? 0) })
                .OrderByDescending(x => x.count).Take(8),
            byMaster = active.Where(a => a.Master != null).GroupBy(a => a.Master!)
                .Select(g => new { name = g.Key, count = g.Count(), revenue = g.Where(a => a.ScheduledAt < now && !a.NoShow).Sum(a => a.Price ?? 0) })
                .OrderByDescending(x => x.count),
            byDay = Enumerable.Range(0, (end - start).Days).Select(d => new
            {
                day = d + 1,
                count = active.Count(a => a.ScheduledAt.Date == start.AddDays(d).Date)
            })
        });
    }
}
