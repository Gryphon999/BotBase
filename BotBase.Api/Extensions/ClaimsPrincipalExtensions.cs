using System.Security.Claims;

namespace BotBase.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetBusinessId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
