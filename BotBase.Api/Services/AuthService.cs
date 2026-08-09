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
            BusinessName = businessName
        };
        db.Businesses.Add(business);
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

    public string GenerateJwt(Business business)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, business.Id.ToString()),
            new Claim(ClaimTypes.Email, business.Email)
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
