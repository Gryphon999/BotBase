using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace BotBase.Api.Filters;

// Отключённый админом бизнес теряет доступ к кабинету сразу, даже с уже выданным токеном.
// Админ (роль admin) и вход админа в кабинет клиента (impersonated_by) проходят всегда.
public class ActiveBusinessFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true
            && !user.IsInRole(AdminAuthService.Role)
            && user.FindFirst(AuthService.ImpersonatedByClaim) is null
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var business = await db.Businesses.FindAsync(id);
            if (business is { IsActive: false })
            {
                context.Result = new ObjectResult(new { error = "Аккаунт отключён администратором." }) { StatusCode = 403 };
                return;
            }
        }
        await next();
    }
}
