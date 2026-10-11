using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BotBase.Api.Services;

public class AdminAuthService(AppDbContext db, IConfiguration config, ILogger<AdminAuthService> log)
{
    public const string Role = "admin";

    // Создаёт админа из переменных Admin__Email / Admin__Password, если его ещё нет.
    // Существующий пароль не перезаписывается, кроме случая Admin__ResetPassword=true.
    public async Task EnsureSeededAsync()
    {
        var email = config["Admin:Email"]?.Trim().ToLower();
        var password = config["Admin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;

        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Email == email);
        if (admin is null)
        {
            db.AdminUsers.Add(new AdminUser { Id = Guid.NewGuid(), Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) });
            log.LogInformation("Admin account created for {Email}", email);
        }
        else if (config.GetValue<bool>("Admin:ResetPassword"))
        {
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            log.LogWarning("Admin password reset for {Email}", email);
        }
        await db.SaveChangesAsync();
    }

    public async Task<AdminUser?> LoginAsync(string email, string password)
    {
        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Email == email.Trim().ToLower());
        return admin is not null && BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash) ? admin : null;
    }

    public string GenerateJwt(AdminUser admin)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new Claim(ClaimTypes.Email, admin.Email),
                new Claim(ClaimTypes.Role, Role)
            ],
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
