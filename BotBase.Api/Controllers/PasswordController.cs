using BotBase.Api.Extensions;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class PasswordController(PasswordService passwords, PlatformSettingsService settings) : ControllerBase
{
    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(ForgotRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Email)) await passwords.RequestResetAsync(req.Email);
        return Ok(new { supportEmail = await settings.GetAsync(PlatformSettingsService.SupportEmail) });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetRequest req) =>
        await passwords.ResetAsync(req.Token, req.NewPassword)
            ? NoContent()
            : BadRequest(new { error = $"Ссылка устарела или пароль короче {PasswordService.MinLength} символов. Запросите восстановление ещё раз." });

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> Change(ChangeRequest req) =>
        await passwords.ChangeAsync(User.GetBusinessId(), req.CurrentPassword, req.NewPassword)
            ? NoContent()
            : BadRequest(new { error = $"Текущий пароль неверный или новый короче {PasswordService.MinLength} символов" });

    public record ForgotRequest(string Email);
    public record ResetRequest(string Token, string NewPassword);
    public record ChangeRequest(string CurrentPassword, string NewPassword);
}
