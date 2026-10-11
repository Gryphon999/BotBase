using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
    builder.WebHost.UseUrls($"http://*:{port}");

var connectionString = GetConnectionString();
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connectionString)
       .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

static string GetConnectionString()
{
    var url = Environment.GetEnvironmentVariable("DATABASE_URL")
              ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (url is not null && (url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                             url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
    }
    return url ?? throw new InvalidOperationException("Connection string not configured");
}

builder.Services.Configure<HostOptions>(opt =>
    opt.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.Configure<ForwardedHeadersOptions>(opt =>
{
    opt.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opt.KnownNetworks.Clear();
    opt.KnownProxies.Clear();
});

builder.Services.AddControllers(opt => opt.Filters.Add<BotBase.Api.Filters.ActiveBusinessFilter>());
builder.Services.AddHttpClient();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<AnthropicService>();
builder.Services.AddScoped<FileParserService>();
builder.Services.AddScoped<CrmWebhookService>();
builder.Services.AddScoped<AdminAuthService>();
builder.Services.AddScoped<PlatformSettingsService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ChatQueue>();
builder.Services.AddSingleton<AiGateway>();
builder.Services.AddSingleton<KnowledgeRetriever>();
builder.Services.AddScoped<BotReplyService>();
builder.Services.AddSingleton<OwnerMessages>();
builder.Services.AddHostedService<BotWorker>();

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
    await scope.ServiceProvider.GetRequiredService<AdminAuthService>().EnsureSeededAsync();
}

app.UseForwardedHeaders();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMethods("/health", new[] { "GET", "HEAD" }, () => Results.Ok());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
