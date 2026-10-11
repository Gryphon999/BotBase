using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace BotBase.Tests;

// Доступ к CRM открывает и закрывает админ (тариф «Бизнес»): не оплатил — не пользуется.
// Автосообщения приходят на языке клиента (армянский / английский / русский).
[Collection("db")]
public class CrmAccessTests(PostgresFixture pg)
{
    record Access(bool Enabled, DateTime? Until);
    record ProcedureDto(Guid Id);

    static async Task<(BotApiFactory app, HttpClient api, HttpClient admin, Guid id)> SetupAsync(PostgresFixture pg)
    {
        var app = new BotApiFactory(pg.NewDatabase());
        var api = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("plan@salon.am", "Салон"));
        (await api.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync("api/schedule", Enumerable.Range(0, 7).Select(d => new { dayOfWeek = d, isWorkingDay = true, startTime = "09:00", endTime = "20:00" }))).EnsureSuccessStatusCode();
        var admin = app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync());
        Guid id;
        using (var scope = app.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Businesses.SingleAsync()).Id;
        return (app, api, admin, id);
    }

    static Task<HttpResponseMessage> SetCrm(HttpClient admin, Guid id, bool enabled, DateTime? until = null) =>
        admin.PostAsJsonAsync($"api/admin/businesses/{id}/crm", new { enabled, until });

    [Fact]
    public async Task Crm_is_closed_until_admin_opens_it_and_closes_after_paid_date()
    {
        var (app, api, admin, id) = await SetupAsync(pg);
        using var _ = app;

        // Новый бизнес — тариф «Старт»: бот и календарь работают, CRM закрыта на сервере.
        Assert.False((await api.GetFromJsonAsync<Access>("api/crm-access"))!.Enabled);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("api/appointments")).StatusCode);
        foreach (var url in new[] { "api/clients", "api/masters", "api/stats", "api/automation" })
        {
            var r = await api.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Contains("crm_locked", await r.Content.ReadAsStringAsync());
        }
        var manual = await api.PostAsJsonAsync("api/appointments/manual", new { clientName = "А", clientPhone = "+37491111111", procedureId = Guid.NewGuid(), scheduledAt = DateTime.Today.AddDays(1).AddHours(10) });
        Assert.Equal(HttpStatusCode.Forbidden, manual.StatusCode);

        // Оплачено до вчера — доступа нет; до завтра — есть.
        var today = BusinessClock.Now(BusinessClock.DefaultZone).Date;
        (await SetCrm(admin, id, true, today.AddDays(-1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("api/clients")).StatusCode);
        (await SetCrm(admin, id, true, today.AddDays(1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("api/clients")).StatusCode);
        var access = (await api.GetFromJsonAsync<Access>("api/crm-access"))!;
        Assert.True(access.Enabled);
        Assert.Equal(today.AddDays(1), access.Until!.Value.Date);

        // Закрыли — снова 403. Клиент не может открыть себе доступ сам.
        (await SetCrm(admin, id, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("api/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync($"api/admin/businesses/{id}/crm", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task Closed_crm_sends_no_automatic_messages_and_bot_ignores_masters()
    {
        var (app, api, admin, id) = await SetupAsync(pg);
        using var _ = app;
        (await SetCrm(admin, id, true)).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("api/procedures", new { name = "Маникюр", durationMinutes = 60, price = 8000 })).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("api/masters", new { name = "Лиана", procedureIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync("api/masters", new { name = "Ани", procedureIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        (await SetCrm(admin, id, false)).EnsureSuccessStatusCode();

        // Напоминание по закрытой CRM не уходит.
        var now = BusinessClock.Now(BusinessClock.DefaultZone);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client { Id = Guid.NewGuid(), BusinessId = id, Name = "Анна", Phone = "+37491111111", PhoneKey = "37491111111", TelegramChatId = 101 };
            db.Clients.Add(client);
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), BusinessId = id, ClientId = client.Id, ProcedureName = "Маникюр", ClientName = "Анна",
                ClientPhone = client.Phone, ScheduledAt = BusinessClock.AsStored(now.AddHours(20)), DurationMinutes = 60, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync();
        }
        await Task.Delay(2500);
        Assert.Empty(app.Telegram.To(101));

        // Без CRM мастера не учитываются: одно общее расписание, второй клиент на то же время не проходит.
        var at = now.Date.AddDays(2).AddHours(12);
        string Marker(string name) => $"{name}, вы записаны!\n[[BOOK:{{\"procedure_name\":\"Маникюр\",\"scheduled_at\":\"{at:yyyy-MM-ddTHH:mm:ss}\",\"duration_minutes\":60,\"client_name\":\"{name}\",\"client_phone\":\"+374 {name.Length}\"}}]]";
        app.Ai.Respond = (_, _) => Marker("Карен");
        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", new { update_id = 1, message = new { message_id = 1, date = 1760000000, chat = new { id = 201, type = "private" }, text = "Запишите" } });
        Assert.Contains("записаны", await app.Telegram.WaitFor(201));
        app.Ai.Respond = (_, _) => Marker("Нарек");
        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", new { update_id = 2, message = new { message_id = 2, date = 1760000000, chat = new { id = 202, type = "private" }, text = "Запишите" } });
        Assert.Contains("занято", await app.Telegram.WaitFor(202));
        Assert.DoesNotContain("МАСТЕРА", app.Ai.Prompts.Last());
    }

    [Theory]
    [InlineData("Բարև, ուզում եմ գրանցվել մանիկյուրի", "Հիշեցնում ենք", "✅ Կգամ")]
    [InlineData("Hi, I'd like to book a manicure please", "A reminder about your appointment", "✅ I'll come")]
    [InlineData("Здравствуйте, хочу на маникюр", "Напоминаем о записи", "✅ Приду")]
    public async Task Reminder_comes_in_the_language_the_client_writes_in(string clientMessage, string expected, string button)
    {
        var (app, api, admin, id) = await SetupAsync(pg);
        using var _ = app;
        (await SetCrm(admin, id, true)).EnsureSuccessStatusCode();
        var now = BusinessClock.Now(BusinessClock.DefaultZone);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conv = new Conversation { Id = Guid.NewGuid(), BusinessId = id, TelegramChatId = 301 };
            db.Conversations.Add(conv);
            db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = "user", Content = clientMessage, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = "assistant", Content = "ok", CreatedAt = DateTime.UtcNow.AddDays(-2).AddSeconds(5) });
            var client = new Client { Id = Guid.NewGuid(), BusinessId = id, Name = "Ани", Phone = "+37491111111", PhoneKey = "37491111111", TelegramChatId = 301 };
            db.Clients.Add(client);
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), BusinessId = id, ClientId = client.Id, ConversationId = conv.Id, ProcedureName = "Маникюр",
                ClientName = "Ани", ClientPhone = client.Phone, ScheduledAt = BusinessClock.AsStored(now.AddHours(20)), DurationMinutes = 60, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync();
        }

        Assert.Contains(expected, await app.Telegram.WaitFor(301, m => m.Contains(expected)));
        Assert.Contains(button, app.Telegram.Buttons.Single(b => b.ChatId == 301).Labels);
    }

    [Fact]
    public async Task Client_who_never_wrote_gets_the_default_language_from_settings()
    {
        var (app, api, admin, id) = await SetupAsync(pg);
        using var _ = app;
        (await SetCrm(admin, id, true)).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync("api/automation", new { timeZone = "Asia/Yerevan", remindersEnabled = true, reviewRequestsEnabled = false,
            winbackEnabled = false, winbackAfterDays = 45, messagesLanguage = "hy" })).EnsureSuccessStatusCode();
        var preview = await api.GetStringAsync("api/automation/preview?lang=en");
        Assert.Contains("A reminder about your appointment", preview);

        var now = BusinessClock.Now(BusinessClock.DefaultZone);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client { Id = Guid.NewGuid(), BusinessId = id, Name = "Ани", Phone = "+37491111111", PhoneKey = "37491111111", TelegramChatId = 401 };
            db.Clients.Add(client);
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), BusinessId = id, ClientId = client.Id, ProcedureName = "Маникюр", ClientName = "Ани",
                ClientPhone = client.Phone, ScheduledAt = BusinessClock.AsStored(now.AddHours(20)), DurationMinutes = 60, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync();
        }
        Assert.Contains("Հիշեցնում ենք", await app.Telegram.WaitFor(401));
    }
}
