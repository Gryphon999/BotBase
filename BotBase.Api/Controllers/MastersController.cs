using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

// Мастера: кто какие процедуры делает и по какому расписанию работает.
// Пока мастеров нет — бизнес работает с одним общим расписанием, как раньше.
[Authorize]
[ApiController]
[Route("api/masters")]
public class MastersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var businessId = User.GetBusinessId();
        var masters = await db.Masters.Include(m => m.Procedures)
            .Where(m => m.BusinessId == businessId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        var ids = masters.Select(m => m.Id).ToList();
        var schedules = await db.WorkSchedules.Where(s => s.MasterId != null && ids.Contains(s.MasterId.Value)).ToListAsync();

        return Ok(masters.Select(m => new MasterDto(
            m.Id, m.Name,
            m.Procedures.Select(p => p.ProcedureId).ToList(),
            Schedule(schedules.Where(s => s.MasterId == m.Id).ToList()))));
    }

    [HttpPost]
    public async Task<IActionResult> Create(MasterRequest req)
    {
        var businessId = User.GetBusinessId();
        if (await Validate(businessId, req) is { } error) return BadRequest(new { error });
        var master = new Master { Id = Guid.NewGuid(), BusinessId = businessId, Name = req.Name.Trim() };
        db.Masters.Add(master);
        Apply(master, req);
        await db.SaveChangesAsync();
        return Ok(new { master.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MasterRequest req)
    {
        var businessId = User.GetBusinessId();
        var master = await db.Masters.Include(m => m.Procedures).FirstOrDefaultAsync(m => m.Id == id && m.BusinessId == businessId && m.IsActive);
        if (master is null) return NotFound();
        if (await Validate(businessId, req) is { } error) return BadRequest(new { error });

        master.Name = req.Name.Trim();
        db.MasterProcedures.RemoveRange(master.Procedures);
        db.WorkSchedules.RemoveRange(await db.WorkSchedules.Where(s => s.MasterId == id).ToListAsync());
        Apply(master, req);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Мастер скрывается, но его прошлые записи и статистика остаются.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = User.GetBusinessId();
        var master = await db.Masters.FirstOrDefaultAsync(m => m.Id == id && m.BusinessId == businessId && m.IsActive);
        if (master is null) return NotFound();
        master.IsActive = false;
        db.WorkSchedules.RemoveRange(await db.WorkSchedules.Where(s => s.MasterId == id).ToListAsync());
        await db.SaveChangesAsync();
        return NoContent();
    }

    async Task<string?> Validate(Guid businessId, MasterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Укажите имя мастера.";
        if (req.Name.Trim().Length > 100) return "Слишком длинное имя.";
        var known = await db.Procedures.Where(p => p.BusinessId == businessId && p.IsActive).Select(p => p.Id).ToListAsync();
        if (req.ProcedureIds.Any(p => !known.Contains(p))) return "Процедура не найдена.";
        if (req.Schedule is { } rows)
        {
            if (rows.Count != 7) return "Расписание: нужно ровно 7 дней.";
            foreach (var r in rows.Where(r => r.IsWorkingDay))
                if (!TimeOnly.TryParse(r.StartTime, out var s) || !TimeOnly.TryParse(r.EndTime, out var e) || s >= e)
                    return "Расписание: проверьте время начала и окончания (HH:mm, начало раньше конца).";
        }
        return null;
    }

    void Apply(Master master, MasterRequest req)
    {
        foreach (var pid in req.ProcedureIds.Distinct())
            db.MasterProcedures.Add(new MasterProcedure { MasterId = master.Id, ProcedureId = pid });
        // null — мастер работает по общему расписанию бизнеса.
        if (req.Schedule is { } rows)
            foreach (var r in rows)
                db.WorkSchedules.Add(new WorkSchedule
                {
                    Id = Guid.NewGuid(), BusinessId = master.BusinessId, MasterId = master.Id, DayOfWeek = r.DayOfWeek,
                    IsWorkingDay = r.IsWorkingDay,
                    StartTime = r.IsWorkingDay ? TimeOnly.Parse(r.StartTime!) : null,
                    EndTime = r.IsWorkingDay ? TimeOnly.Parse(r.EndTime!) : null
                });
    }

    static List<ScheduleRow>? Schedule(List<WorkSchedule> rows) => rows.Count == 0 ? null
        : rows.OrderBy(r => r.DayOfWeek).Select(r => new ScheduleRow(r.DayOfWeek, r.IsWorkingDay, r.StartTime?.ToString("HH:mm"), r.EndTime?.ToString("HH:mm"))).ToList();

    public record ScheduleRow(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
    public record MasterRequest(string Name, List<Guid> ProcedureIds, List<ScheduleRow>? Schedule);
    public record MasterDto(Guid Id, string Name, List<Guid> ProcedureIds, List<ScheduleRow>? Schedule);
}
