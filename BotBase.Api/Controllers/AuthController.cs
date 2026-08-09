using BotBase.Api.Models.Auth;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var business = await auth.RegisterAsync(req.Email, req.Password, req.BusinessName);
        if (business is null)
            return BadRequest(new { error = "Email уже используется" });
        var token = auth.GenerateJwt(business);
        return Ok(new LoginResponse(token, business.BusinessName));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var business = await auth.LoginAsync(req.Email, req.Password);
        if (business is null)
            return Unauthorized(new { error = "Неверный email или пароль" });
        var token = auth.GenerateJwt(business);
        return Ok(new LoginResponse(token, business.BusinessName));
    }
}
