using BotBase.Api.Data;
using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

// Кабинету нужно знать, открыт ли раздел CRM (показать его или предложение подключить тариф «Бизнес»).
[Authorize]
[ApiController]
[Route("api/crm-access")]
public class CrmAccessController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var b = await db.Businesses.FindAsync(User.GetBusinessId());
        if (b is null) return NotFound();
        return Ok(new { enabled = b.HasCrm(BusinessClock.Now(b.TimeZone)), until = b.CrmEnabled ? b.CrmUntil : null });
    }
}
