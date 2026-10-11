using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace BotBase.Api.Filters;

// Раздел CRM (тариф «Бизнес») доступен, только если админ открыл его бизнесу и срок оплаты не истёк.
// Проверка на сервере — закрытый раздел нельзя «открыть» из браузера. Вход админа в кабинет видит то же, что клиент.
public class RequiresCrmAttribute() : TypeFilterAttribute(typeof(RequiresCrmFilter));

public class RequiresCrmFilter(AppDbContext db) : IAsyncActionFilter
{
    public const string LockedCode = "crm_locked";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (Guid.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && await db.Businesses.FindAsync(id) is { } business
            && business.HasCrm(BusinessClock.Now(business.TimeZone)))
        {
            await next();
            return;
        }
        context.Result = new ObjectResult(new { error = "CRM доступна на тарифе «Бизнес». Чтобы подключить, напишите в поддержку.", code = LockedCode })
            { StatusCode = 403 };
    }
}
