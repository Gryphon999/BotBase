using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = GetConnectionString();
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));

static string GetConnectionString()
{
    var url = Environment.GetEnvironmentVariable("DATABASE_URL")
              ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (url is not null && url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':');
        return $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
    }
    return url ?? throw new InvalidOperationException("Connection string not configured");
}

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<AnthropicService>();
builder.Services.AddScoped<FileParserService>();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opt =>
    opt.MultipartBodyLengthLimit = 10_000_000);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
