using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BotBase.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    public async Task<Business?> RegisterAsync(string email, string password, string businessName)
    {
        if (await db.Businesses.AnyAsync(b => b.Email == email.ToLower()))
            return null;

        var business = new Business
        {
            Id = Guid.NewGuid(),
            Email = email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            BusinessName = businessName,
            TermsAcceptedAt = DateTime.UtcNow
        };
        db.Businesses.Add(business);
        var defaultSchedule = Enumerable.Range(0, 7).Select(day => new WorkSchedule
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            DayOfWeek = day,
            IsWorkingDay = day < 5,
            StartTime = day < 5 ? new TimeOnly(9, 0) : null,
            EndTime = day < 5 ? new TimeOnly(18, 0) : null
        });
        db.WorkSchedules.AddRange(defaultSchedule);
        await db.SaveChangesAsync();
        return business;
    }

    public async Task<Business?> LoginAsync(string email, string password)
    {
        var business = await db.Businesses
            .FirstOrDefaultAsync(b => b.Email == email.ToLower());
        if (business is null) return null;
        return BCrypt.Net.BCrypt.Verify(password, business.PasswordHash) ? business : null;
    }

    public string GenerateJwt(Business business) => BuildToken(business, DateTime.UtcNow.AddYears(2));

    // Токен для входа администратора в кабинет клиента: короткий срок и отметка, кто вошёл.
    public string GenerateImpersonationJwt(Business business, string adminEmail) =>
        BuildToken(business, DateTime.UtcNow.AddHours(1), new Claim(ImpersonatedByClaim, adminEmail));

    public const string ImpersonatedByClaim = "impersonated_by";

    public string GenerateApiKey(Business business) => BuildToken(business, DateTime.UtcNow.AddYears(1));

    private string BuildToken(Business business, DateTime expires, params Claim[] extra)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, business.Id.ToString()),
            new Claim(ClaimTypes.Email, business.Email),
            .. extra
        ];
        var token = new JwtSecurityToken(
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
