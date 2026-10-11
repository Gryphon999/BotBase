using BotBase.Api.Data;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace BotBase.Tests;

// Просьба 11.10.2026: если владелец потерял адрес сайта, ссылка на кабинет должна быть у него в Telegram.
[Collection("db")]
public class OwnerCabinetLinkTests(PostgresFixture pg)
{
    const long Owner = 1, Client = 2;
    const string Link = "https://botbase.test/dashboard";

    static object Update(long chatId, string text) => new
    {
        update_id = Random.Shared.Next(),
        message = new { message_id = Random.Shared.Next(), date = 1760000000, chat = new { id = chatId, type = "private" }, text }
    };

    [Fact]
    public async Task Owner_gets_cabinet_link_and_clients_do_not()
    {
        using var app = new BotApiFactory(pg.NewDatabase());
        var api = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("owner@salon.am", "Салон"));
        (await api.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        Guid id;
        using (var scope = app.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Businesses.SingleAsync()).Id;
        var http = app.CreateClient();

        // Подключение владельца — ссылка сразу в приветствии.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Owner, "/start"));
        Assert.Contains(Link, app.Telegram.To(Owner).Single());

        // Потерял ссылку — пишет /cabinet (или /кабинет) и получает её снова, без обращения к ИИ.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Owner, "/cabinet"));
        await http.PostAsJsonAsync($"webhook/{id}", Update(Owner, "/кабинет"));
        Assert.Equal(3, app.Telegram.To(Owner).Count(m => m.Contains(Link)));

        // Повторный /start владельца не превращается в разговор с ИИ.
        await http.PostAsJsonAsync($"webhook/{id}", Update(Owner, "/start"));
        Assert.Equal(4, app.Telegram.To(Owner).Count(m => m.Contains(Link)));

        // Клиент ссылку на чужой кабинет не получает — его сообщение обрабатывает бот как обычно.
        app.Ai.Respond = (_, _) => "Здравствуйте! Чем помочь?";
        await http.PostAsJsonAsync($"webhook/{id}", Update(Client, "/cabinet"));
        Assert.Equal("Здравствуйте! Чем помочь?", await app.Telegram.WaitFor(Client));
        Assert.DoesNotContain(app.Telegram.To(Client), m => m.Contains(Link));
        Assert.Equal(1, app.Ai.Calls);
    }
}
