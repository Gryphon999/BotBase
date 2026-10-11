using BotBase.Api.Models.Auth;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, PlatformSettingsService settings) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        // Без проверки на проде появился аккаунт с email «test» — войти в него нельзя (браузер не отправит такой email).
        if (string.IsNullOrWhiteSpace(req.BusinessName))
            return BadRequest(new { error = "Укажите название бизнеса" });
        if (!IsValidEmail(req.Email))
            return BadRequest(new { error = "Укажите корректный email — он нужен для входа и восстановления пароля" });
        if (req.Password is not { Length: >= PasswordService.MinLength })
            return BadRequest(new { error = $"Пароль должен быть не короче {PasswordService.MinLength} символов" });

        var business = await auth.RegisterAsync(req.Email, req.Password, req.BusinessName);
        if (business is null)
            return BadRequest(new { error = "Email уже используется" });
        var token = auth.GenerateJwt(business);
        return Ok(new LoginResponse(token, business.BusinessName));
    }

    static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var addr)
        && addr.Address == email.Trim()
        && addr.Host.Contains('.');

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var business = await auth.LoginAsync(req.Email, req.Password);
        if (business is null)
            return Unauthorized(new { error = "Неверный email или пароль" });
        if (!business.IsActive)
        {
            var support = await settings.GetAsync(PlatformSettingsService.SupportEmail);
            return StatusCode(403, new { error = "Аккаунт отключён администратором." + (support is null ? "" : $" Напишите в поддержку: {support}") });
        }
        var token = auth.GenerateJwt(business);
        return Ok(new LoginResponse(token, business.BusinessName));
    }
}
