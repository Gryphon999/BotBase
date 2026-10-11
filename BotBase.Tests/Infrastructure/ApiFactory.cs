using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
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
