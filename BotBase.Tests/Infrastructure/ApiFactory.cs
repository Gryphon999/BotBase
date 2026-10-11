using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BotBase.Tests.Infrastructure;

// Поднимает настоящее приложение BotBase поверх указанной базы — как один «деплой».
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@botbase.test";
    public const string AdminPassword = "Admin-Pass-123";

    public ApiFactory(string connectionString)
    {
        // Program.cs читает строку подключения напрямую из переменных окружения.
        Environment.SetEnvironmentVariable("DATABASE_URL", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Admin__Email", AdminEmail);
        Environment.SetEnvironmentVariable("Admin__Password", AdminPassword);
        Environment.SetEnvironmentVariable("WebhookBaseUrl", "https://botbase.test");
        // Короткие паузы бота, чтобы тесты шли быстро (в проде — секунды).
        Environment.SetEnvironmentVariable("Bot__DebounceMs", "300");
        Environment.SetEnvironmentVariable("Bot__MaxDebounceMs", "1500");
        Environment.SetEnvironmentVariable("Bot__BusyNoticeAfterSec", "1");
        Environment.SetEnvironmentVariable("Bot__GiveUpAfterSec", "5");
        Environment.SetEnvironmentVariable("Bot__RequestsPerMinute", "1000");
        Environment.SetEnvironmentVariable("Bot__TokensPerMinute", "100000000");
        // Автосообщения: проверка каждую секунду, без «тихих часов» (тесты идут в любое время суток).
        Environment.SetEnvironmentVariable("Bot__AutomationIntervalSec", "1");
        Environment.SetEnvironmentVariable("Bot__QuietHoursEnd", "0");
        Environment.SetEnvironmentVariable("Bot__QuietHoursStart", "24");
        // Короткие паузы бота, чтобы тесты шли быстро (в проде — секунды).
        Environment.SetEnvironmentVariable("Bot__DebounceMs", "300");
        Environment.SetEnvironmentVariable("Bot__MaxDebounceMs", "1500");
        Environment.SetEnvironmentVariable("Bot__BusyNoticeAfterSec", "1");
        Environment.SetEnvironmentVariable("Bot__GiveUpAfterSec", "5");
    }

    // Ошибки из логов приложения (фоновые обработчики глотают исключения — так тест видит, что именно упало).
    public System.Collections.Concurrent.ConcurrentQueue<string> LoggedErrors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(l => l.AddProvider(new ErrorCollector(LoggedErrors)));
    }

    sealed class ErrorCollector(System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string category) => new Logger(sink);
        public void Dispose() { }
        sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<T>(T state) where T : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
            public void Log<T>(LogLevel level, EventId id, T state, Exception? ex, Func<T, Exception?, string> fmt)
            {
                if (level >= LogLevel.Error) sink.Enqueue(fmt(state, ex) + " " + ex);
            }
        }
    }
}

public static class HttpExtensions
{
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> RegisterAsync(this HttpClient client, string email, string name, string password = "Pass-12345")
    {
        var resp = await client.PostAsJsonAsync("api/auth/register", new { email, password, businessName = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
    }

    public static async Task<string> AdminLoginAsync(this HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("api/admin/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
    }

    public record TokenResponse(string Token);
}
