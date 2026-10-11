using BotBase.Api.Models.Auth;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController(AdminAuthService auth) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(AdminLoginRequest req)
    {
        var admin = await auth.LoginAsync(req.Email, req.Password);
        if (admin is null)
            return Unauthorized(new { error = "Неверный email или пароль" });
        return Ok(new { token = auth.GenerateJwt(admin), email = admin.Email });
    }

    public record AdminLoginRequest(string Email, string Password);
}
