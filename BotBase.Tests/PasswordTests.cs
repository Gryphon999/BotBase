using BotBase.Api.Services;
using BotBase.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace BotBase.Tests;

public class Mailbox { public ConcurrentQueue<(string To, string Subject, string Body)> Emails { get; } = new(); }

public class CapturingNotification(IConfiguration c, PlatformSettingsService s, TelegramService t, ILogger<NotificationService> l, Mailbox box)
    : NotificationService(c, s, t, l)
{
    public override Task SendEmailAsync(string to, string subject, string body)
    {
        box.Emails.Enqueue((to, subject, body));
        return Task.CompletedTask;
    }
}

public class MailApiFactory(string cs) : ApiFactory(cs)
{
    public Mailbox Mailbox { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton(Mailbox);
            s.AddScoped<NotificationService, CapturingNotification>();
        });
    }
}

[Collection("db")]
public class PasswordTests(PostgresFixture pg)
{
    static Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("api/auth/login", new { email, password });

    [Fact]
    public async Task Forgot_password_link_sets_new_password_once()
    {
        using var app = new MailApiFactory(pg.NewDatabase());
        var http = app.CreateClient();
        await http.RegisterAsync("owner@salon.am", "Салон");

        // Для несуществующего email ответ такой же — нельзя проверить, кто зарегистрирован.
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("api/auth/forgot-password", new { email = "nobody@x.am" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("api/auth/forgot-password", new { email = "Owner@Salon.am" })).StatusCode);

        var mail = Assert.Single(app.Mailbox.Emails);
        Assert.Equal("owner@salon.am", mail.To);
        var token = Regex.Match(mail.Body, @"reset-password\?token=([\w-]+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token));

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("api/auth/reset-password", new { token = "wrong", newPassword = "NewPass-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("api/auth/reset-password", new { token, newPassword = "NewPass-123" })).StatusCode);
        // Ссылка одноразовая.
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("api/auth/reset-password", new { token, newPassword = "Other-456" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(http, "owner@salon.am", "Pass-12345")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(http, "owner@salon.am", "NewPass-123")).StatusCode);
    }

    [Fact]
    public async Task Reset_link_also_goes_to_owner_telegram_when_email_is_not_set_up()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var http = app.CreateClient();
        var client = app.CreateClient().WithToken(await http.RegisterAsync("crio@salon.am", "Крио"));
        (await client.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        var id = (await app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync())
            .GetFromJsonAsync<List<BizDto>>("api/admin/businesses"))!.Single().Id;
        await http.PostAsJsonAsync($"webhook/{id}", new { update_id = 1, message = new { message_id = 1, date = 1760000000, chat = new { id = 555, type = "private" }, text = "/start" } });

        (await http.PostAsJsonAsync("api/auth/forgot-password", new { email = "crio@salon.am" })).EnsureSuccessStatusCode();

        var msg = Assert.Single(app.Telegram.To(555), m => m.Contains("reset-password?token="));
        var token = Regex.Match(msg, @"token=([\w-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("api/auth/reset-password", new { token, newPassword = "ViaTelegram-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(http, "crio@salon.am", "ViaTelegram-1")).StatusCode);
    }

    [Fact]
    public async Task Admin_temporary_password_lets_client_in()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        await app.CreateClient().RegisterAsync("forgot@salon.am", "Забывчивый");
        var admin = app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync());
        var id = (await admin.GetFromJsonAsync<List<BizDto>>("api/admin/businesses"))!.Single().Id;

        var temp = (await (await admin.PostAsync($"api/admin/businesses/{id}/reset-password", null)).Content.ReadFromJsonAsync<TempDto>())!.TemporaryPassword;
        Assert.Equal(10, temp.Length);
        Assert.Equal(HttpStatusCode.OK, (await Login(app.CreateClient(), "forgot@salon.am", temp)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(app.CreateClient(), "forgot@salon.am", "Pass-12345")).StatusCode);

        // Клиентский токен не может сбрасывать пароли.
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("other@salon.am", "Другой"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"api/admin/businesses/{id}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_current_one()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("me@salon.am", "Я"));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/auth/change-password", new { currentPassword = "wrong", newPassword = "Fresh-789" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("api/auth/change-password", new { currentPassword = "Pass-12345", newPassword = "Fresh-789" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(app.CreateClient(), "me@salon.am", "Fresh-789")).StatusCode);
    }

    record BizDto(Guid Id);
    record TempDto(string TemporaryPassword);
}
