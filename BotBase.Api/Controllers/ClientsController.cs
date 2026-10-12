using BotBase.Api.Filters;
using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

// База клиентов: карточки появляются сами из записей (бот, вручную, CRM) — по номеру телефона.
[Authorize]
[RequiresCrm]
[ApiController]
[Route("api/clients")]
public class ClientsController(AppDbContext db, BookingService booking, ClientDataService clientData) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        var businessId = User.GetBusinessId();
        var business = await db.Businesses.FindAsync(businessId);
        var now = BusinessClock.Now(business!.TimeZone);

        var query = db.Clients.Where(c => c.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            var digits = Client.KeyOf(s);
            query = query.Where(c => c.Name.ToLower().Contains(s) || (digits.Length >= 3 && c.PhoneKey.Contains(digits)));
        }

        var clients = await query
            .Select(c => new
            {
                c.Id, c.Name, c.Phone, c.Notes, c.CreatedAt,
                HasTelegram = c.TelegramChatId != null,
                Visits = c.Appointments.Where(a => a.Status != AppointmentStatus.Cancelled && !a.NoShow && a.ScheduledAt < now)
                    .Select(a => new { a.ScheduledAt, a.Price }).ToList(),
                Next = c.Appointments.Where(a => a.Status != AppointmentStatus.Cancelled && a.ScheduledAt >= now)
                    .OrderBy(a => a.ScheduledAt).Select(a => (DateTime?)a.ScheduledAt).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(clients
            .Select(c => new ClientListItem(c.Id, c.Name, c.Phone, c.HasTelegram, c.Visits.Count,
                c.Visits.Count == 0 ? null : c.Visits.Max(v => v.ScheduledAt), c.Next,
                c.Visits.Sum(v => v.Price ?? 0), c.Notes))
            // Сначала те, у кого запись ближе всего; дальше — по давности последнего визита.
            .OrderBy(c => c.NextVisit is null)
            .ThenBy(c => c.NextVisit)
            .ThenByDescending(c => c.LastVisit));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var businessId = User.GetBusinessId();
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (c is null) return NotFound();
        return Ok(new { c.Id, c.Name, c.Phone, c.Notes, c.CreatedAt, HasTelegram = c.TelegramChatId != null });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ClientRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || Client.KeyOf(req.Phone ?? "").Length < 5)
            return BadRequest(new { error = "Укажите имя и номер телефона." });
        var businessId = User.GetBusinessId();
        if (await db.Clients.AnyAsync(c => c.BusinessId == businessId && c.PhoneKey == Client.KeyOf(req.Phone!)))
            return Conflict(new { error = "Клиент с таким номером уже есть." });
        var client = await booking.UpsertClientAsync(businessId, req.Name, req.Phone!, null);
        client.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        await db.SaveChangesAsync();
        return Ok(new { client.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ClientRequest req)
    {
        var businessId = User.GetBusinessId();
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (c is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Имя не может быть пустым." });

        if (req.Phone is not null && Client.KeyOf(req.Phone) != c.PhoneKey)
        {
            var key = Client.KeyOf(req.Phone);
            if (key.Length < 5) return BadRequest(new { error = "Проверьте номер телефона." });
            if (await db.Clients.AnyAsync(x => x.BusinessId == businessId && x.PhoneKey == key && x.Id != id))
                return Conflict(new { error = "Клиент с таким номером уже есть." });
            c.Phone = req.Phone.Trim();
            c.PhoneKey = key;
        }
        c.Name = req.Name.Trim();
        c.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Клиент попросил удалить его данные: карточка и переписка удаляются, в записях стираются имя и телефон.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) => ToResponse(await clientData.EraseClientAsync(User.GetBusinessId(), id));

    internal static IActionResult ToResponse(EraseResult result) => result switch
    {
        EraseResult.Done => new NoContentResult(),
        EraseResult.NotFound => new NotFoundResult(),
        _ => new ConflictObjectResult(new { error = "У клиента есть предстоящие записи — сначала отмените их, потом удалите данные." })
    };

    public record ClientRequest(string Name, string? Phone, string? Notes);

    public record ClientListItem(Guid Id, string Name, string Phone, bool HasTelegram, int Visits,
        DateTime? LastVisit, DateTime? NextVisit, decimal TotalSpent, string? Notes);
}
