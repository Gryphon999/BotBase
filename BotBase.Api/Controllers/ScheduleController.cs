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
public class ScheduleController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var businessId = User.GetBusinessId();
        var schedule = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DayOfWeek)
            .Select(s => new
            {
                s.DayOfWeek,
                s.IsWorkingDay,
                StartTime = s.StartTime.HasValue ? s.StartTime.Value.ToString("HH:mm") : null,
                EndTime = s.EndTime.HasValue ? s.EndTime.Value.ToString("HH:mm") : null
            })
            .ToListAsync();
        return Ok(schedule);
    }

    [HttpPut]
    public async Task<IActionResult> Update(List<ScheduleRowRequest> rows)
    {
        if (rows.Count != 7)
            return BadRequest(new { error = "Должно быть ровно 7 строк (Пн–Вс)" });

        foreach (var row in rows.Where(r => r.IsWorkingDay))
        {
            if (string.IsNullOrEmpty(row.StartTime) || string.IsNullOrEmpty(row.EndTime))
                return BadRequest(new { error = "Для рабочего дня укажите время начала и окончания" });
            if (!TimeOnly.TryParse(row.StartTime, out var start) || !TimeOnly.TryParse(row.EndTime, out var end))
                return BadRequest(new { error = "Неверный формат времени. Используйте HH:mm" });
            if (start >= end)
                return BadRequest(new { error = "Время начала должно быть меньше времени окончания" });
        }

        var businessId = User.GetBusinessId();
        var existing = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .ToListAsync();
        db.WorkSchedules.RemoveRange(existing);

        var newRows = rows.Select(r =>
        {
            TimeOnly.TryParse(r.StartTime, out var start);
            TimeOnly.TryParse(r.EndTime, out var end);
            return new WorkSchedule
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                DayOfWeek = r.DayOfWeek,
                IsWorkingDay = r.IsWorkingDay,
                StartTime = r.IsWorkingDay ? start : null,
                EndTime = r.IsWorkingDay ? end : null
            };
        });
        db.WorkSchedules.AddRange(newRows);
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record ScheduleRowRequest(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
}
