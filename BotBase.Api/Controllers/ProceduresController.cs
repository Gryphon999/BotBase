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
public class ProceduresController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var businessId = User.GetBusinessId();
        var procedures = await db.Procedures
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.DurationMinutes, p.Price })
            .ToListAsync();
        return Ok(procedures);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProcedureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.DurationMinutes <= 0 || req.Price < 0)
            return BadRequest(new { error = "Проверьте данные: название обязательно, длительность > 0, цена >= 0" });

        var procedure = new Procedure
        {
            Id = Guid.NewGuid(),
            BusinessId = User.GetBusinessId(),
            Name = req.Name.Trim(),
            DurationMinutes = req.DurationMinutes,
            Price = req.Price
        };
        db.Procedures.Add(procedure);
        await db.SaveChangesAsync();
        return Ok(new { procedure.Id, procedure.Name, procedure.DurationMinutes, procedure.Price });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, ProcedureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.DurationMinutes <= 0 || req.Price < 0)
            return BadRequest(new { error = "Проверьте данные: название обязательно, длительность > 0, цена >= 0" });

        var businessId = User.GetBusinessId();
        var procedure = await db.Procedures
            .FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == businessId && p.IsActive);
        if (procedure is null) return NotFound();

        procedure.Name = req.Name.Trim();
        procedure.DurationMinutes = req.DurationMinutes;
        procedure.Price = req.Price;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = User.GetBusinessId();
        var procedure = await db.Procedures
            .FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == businessId && p.IsActive);
        if (procedure is null) return NotFound();

        procedure.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record ProcedureRequest(string Name, int DurationMinutes, decimal Price);
}
