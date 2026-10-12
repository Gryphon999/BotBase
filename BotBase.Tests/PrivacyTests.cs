using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace BotBase.Tests;

// 12.10.2026, закон РА «О защите персональных данных»: согласие владельца при регистрации,
// уведомление клиента в боте со ссылкой на политику, удаление данных клиента по его просьбе.
[Collection("db")]
public class PrivacyTests(PostgresFixture pg)
{
    const long Owner = 1, Anna = 101, Boris = 102;
    const string Policy = "https://botbase.test/privacy";

    static object Update(long chatId, string text) => new
    {
        update_id = Random.Shared.Next(),
        message = new { message_id = Random.Shared.Next(), date = 1760000000, chat = new { id = chatId, type = "private" }, text }
    };

    static async Task<T> Db<T>(BotApiFactory app, Func<AppDbContext, Task<T>> f)
    {
        using var scope = app.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    static async Task<(HttpClient api, Guid id)> SalonAsync(BotApiFactory app)
    {
        var api = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("owner@salon.am", "Салон"));
        (await api.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        var id = await Db(app, db => db.Businesses.Select(b => b.Id).SingleAsync());
        return (api, id);
    }

    [Fact]
    public async Task Registration_requires_accepting_terms_and_remembers_when()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var http = app.CreateClient();

        var refused = await http.PostAsJsonAsync("api/auth/register", new { email = "a@salon.am", password = "Pass-12345", businessName = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("политику конфиденциальности", await refused.Content.ReadAsStringAsync());
        Assert.False(await Db(app, db => db.Businesses.AnyAsync()));

        var before = DateTime.UtcNow;
        (await http.PostAsJsonAsync("api/auth/register", new { email = "a@salon.am", password = "Pass-12345", businessName = "A", acceptTerms = true }))
            .EnsureSuccessStatusCode();
        Assert.True(await Db(app, db => db.Businesses.Select(b => b.TermsAcceptedAt).SingleAsync()) >= before);
    }

    [Fact]
    public async Task Client_gets_privacy_notice_once_in_own_language_before_the_answer()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var (_, id) = await SalonAsync(app);
        var http = app.CreateClient();
        app.Ai.Respond = (_, _) => "Здравствуйте!";

        // Владелец подключается — ему уведомление для клиентов не нужно.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Owner, "/start"));
        Assert.DoesNotContain(app.Telegram.AllTo(Owner), m => m.Contains(Policy));

        // Клиент пишет по-армянски: уведомление по-армянски, со ссылкой, раньше ответа бота.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Anna, "Բարև ձեզ, ուզում եմ գրանցվել"));
        await app.Telegram.WaitFor(Anna, m => m == "Здравствуйте!");
        var anna = app.Telegram.AllTo(Anna);
        Assert.Contains(Policy, anna[0]);
        Assert.Contains("Google Gemini", anna[0]);
        Assert.Contains("մշակում ենք", anna[0]);

        // Второе сообщение — без повторного уведомления.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Anna, "Իսկ վաղը՞"));
        await app.Telegram.WaitFor(Anna, m => app.Telegram.To(Anna).Count(x => x == "Здравствуйте!") == 2);
        Assert.Single(app.Telegram.AllTo(Anna), m => m.Contains(Policy));

        // /start ничего не говорит о языке — уведомление на языке бизнеса по умолчанию (русский).
        await http.PostAsJsonAsync($"webhook/{id}", Update(Boris, "/start"));
        await app.Telegram.WaitFor(Boris, m => m == "Здравствуйте!");
        Assert.Contains("Продолжая диалог", app.Telegram.AllTo(Boris)[0]);
    }

    [Fact]
    public async Task Erasing_client_removes_chat_and_card_but_keeps_anonymous_visits()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var (api, id) = await SalonAsync(app);
        var http = app.CreateClient();
        await http.PostAsJsonAsync($"webhook/{id}", Update(Anna, "Здравствуйте, хочу на маникюр"));
        await app.Telegram.WaitFor(Anna, m => m == "Здравствуйте!");

        var (conversationId, clientId, futureId) = await Db(app, async db =>
        {
            var conv = await db.Conversations.SingleAsync(c => c.TelegramChatId == Anna);
            var client = new Client { Id = Guid.NewGuid(), BusinessId = id, Name = "Анна", Phone = "+374 91 123456", PhoneKey = "37491123456", TelegramChatId = Anna, Notes = "аллергия" };
            db.Clients.Add(client);
            var now = BusinessClock.Now("Asia/Yerevan");
            Appointment Visit(DateTime at) => new()
            {
                Id = Guid.NewGuid(), BusinessId = id, ClientId = client.Id, ConversationId = conv.Id, ProcedureName = "Маникюр",
                ClientName = "Анна", ClientPhone = "+374 91 123456", ScheduledAt = at, DurationMinutes = 60, Price = 5000
            };
            var past = Visit(now.Date.AddDays(-10).AddHours(11));
            var future = Visit(now.Date.AddDays(3).AddHours(11));
            db.Appointments.AddRange(past, future);
            await db.SaveChangesAsync();
            return (conv.Id, client.Id, future.Id);
        });

        // Будущий визит не теряем молча: сначала его нужно отменить.
        var blocked = await api.DeleteAsync($"api/conversations/{conversationId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.True(await Db(app, db => db.Clients.AnyAsync()));

        await Db(app, async db =>
        {
            (await db.Appointments.SingleAsync(a => a.Id == futureId)).Status = AppointmentStatus.Cancelled;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"api/conversations/{conversationId}")).StatusCode);

        Assert.False(await Db(app, db => db.Conversations.AnyAsync()));
        Assert.False(await Db(app, db => db.Messages.AnyAsync()));
        Assert.False(await Db(app, db => db.Clients.AnyAsync(c => c.Id == clientId)));
        var visits = await Db(app, db => db.Appointments.ToListAsync());
        Assert.Equal(2, visits.Count);
        Assert.All(visits, a => Assert.Equal((ClientDataService.ErasedName, "", (Guid?)null, (Guid?)null, 5000m),
            (a.ClientName, a.ClientPhone, a.ClientId, a.ConversationId, a.Price!.Value)));

        // Повторное удаление — уже нечего удалять.
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"api/conversations/{conversationId}")).StatusCode);
    }

    [Fact]
    public async Task Erasing_from_client_card_works_with_crm_and_only_for_own_clients()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var (api, id) = await SalonAsync(app);
        var clientId = await Db(app, async db =>
        {
            (await db.Businesses.SingleAsync()).CrmEnabled = true;
            var client = new Client { Id = Guid.NewGuid(), BusinessId = id, Name = "Борис", Phone = "091 000 111", PhoneKey = "091000111" };
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        });

        // Чужой салон не может удалить нашего клиента.
        var other = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("other@salon.am", "Другой"));
        await Db(app, async db => { (await db.Businesses.SingleAsync(b => b.Email == "other@salon.am")).CrmEnabled = true; return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/clients/{clientId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"api/clients/{clientId}")).StatusCode);
        Assert.False(await Db(app, db => db.Clients.AnyAsync()));
    }
}
