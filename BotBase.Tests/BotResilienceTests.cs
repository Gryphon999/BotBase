using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace BotBase.Tests;

// Жалоба 11.10.2026: каждый 8-й ответ бота был «Извините, сервис временно недоступен» (лимиты бесплатного Gemini).
[Collection("db")]
public class BotResilienceTests(PostgresFixture pg)
{
    const string OldError = "сервис временно недоступен";

    static object Update(long chatId, string text) => new
    {
        update_id = Random.Shared.Next(),
        message = new { message_id = Random.Shared.Next(), date = 1760000000, chat = new { id = chatId, type = "private" }, text }
    };

    static async Task<(BotApiFactory app, Guid id)> SetupAsync(PostgresFixture pg, string? knowledge = null)
    {
        var app = new BotApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("bot@salon.am", "Салон"));
        (await client.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var biz = await db.Businesses.SingleAsync();
        if (knowledge is not null)
        {
            db.KnowledgeChunks.Add(new KnowledgeChunk { Id = Guid.NewGuid(), BusinessId = biz.Id, FileName = "kb.txt", ExtractedText = knowledge });
            await db.SaveChangesAsync();
        }
        return (app, biz.Id);
    }

    [Fact]
    public async Task Burst_of_messages_gets_one_answer()
    {
        var (app, id) = await SetupAsync(pg);
        using var _ = app;
        var http = app.CreateClient();
        app.Ai.Respond = (_, h) => $"Ответ на {h.Count(m => m.role == "user")} сообщения";

        foreach (var text in new[] { "Здравствуйте", "хочу записаться", "на завтра" })
            await http.PostAsJsonAsync($"webhook/{id}", Update(7, text));

        var reply = await app.Telegram.WaitFor(7);
        await Task.Delay(800);
        Assert.Equal(1, app.Ai.Calls);
        Assert.Single(app.Telegram.To(7));
        Assert.Equal("Ответ на 3 сообщения", reply);
        Assert.True(app.Telegram.Typing > 0); // клиент видел «печатает…»
    }

    [Fact]
    public async Task Rate_limit_errors_are_retried_and_client_gets_real_answer()
    {
        var (app, id) = await SetupAsync(pg);
        using var _ = app;
        app.Ai.FailNext = 2; // два раза 429, потом ответ
        app.Ai.Respond = (_, _) => "Свободно завтра в 14:00";

        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(8, "Есть время завтра?"));

        var reply = await app.Telegram.WaitFor(8, m => m.Contains("14:00"));
        Assert.DoesNotContain(app.Telegram.To(8), m => m.Contains(OldError));
        Assert.Equal("Свободно завтра в 14:00", reply);
    }

    [Fact]
    public async Task Long_outage_shows_busy_notice_then_polite_message_never_old_error()
    {
        var (app, id) = await SetupAsync(pg);
        using var _ = app;
        app.Ai.AlwaysFail = true;

        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(9, "Здравствуйте"));

        await app.Telegram.WaitFor(9, m => m == BotWorker.GiveUpNotice, 20000);
        var msgs = app.Telegram.To(9);
        Assert.Contains(BotWorker.BusyNotice, msgs);
        Assert.DoesNotContain(msgs, m => m.Contains(OldError));
    }

    [Fact]
    public async Task Only_relevant_knowledge_goes_to_ai_for_large_bases()
    {
        var filler = string.Join("\n", Enumerable.Range(0, 400).Select(i => $"Раздел {i}. Описание процедуры номер {i}: состав сыворотки, техника нанесения, уход после процедуры."));
        var kb = "Студия «Крио». Адрес: Ереван, Пушкина 10.\n" + filler + "\nПарковка: бесплатная парковка во дворе, въезд с улицы Туманяна.\n" + filler;
        var (app, id) = await SetupAsync(pg, kb);
        using var _ = app;
        app.Ai.Respond = (_, _) => "Да, есть парковка во дворе";

        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(10, "У вас есть парковка?"));
        await app.Telegram.WaitFor(10);

        var prompt = Assert.Single(app.Ai.Prompts);
        var budget = app.Services.GetRequiredService<IOptions<BotOptions>>().Value.KnowledgeBudgetChars;
        Assert.Contains("въезд с улицы Туманяна", prompt);   // нужный кусок найден
        Assert.Contains("Пушкина 10", prompt);               // начало базы (о нас, адрес) всегда в промпте
        Assert.True(kb.Length > budget * 5);
        Assert.True(prompt.Length < budget + 6000, $"промпт {prompt.Length} символов — база ушла целиком");
    }

    [Fact]
    public async Task Unanswered_message_is_picked_up_after_restart()
    {
        var cs = pg.NewDatabase();
        Guid id;
        using (var before = new BotApiFactory(cs))
        {
            var client = before.CreateClient().WithToken(await before.CreateClient().RegisterAsync("r@salon.am", "Рестарт"));
            (await client.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
            using var scope = before.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await db.Businesses.SingleAsync()).Id;
            // Сообщение сохранилось, но сервер «упал» до ответа (деплой).
            var conv = new Conversation { Id = Guid.NewGuid(), BusinessId = id, TelegramChatId = 11 };
            db.Conversations.Add(conv);
            db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, Role = "user", Content = "Вы работаете в субботу?" });
            await db.SaveChangesAsync();
        }

        using var after = new BotApiFactory(cs);
        after.Ai.Respond = (_, _) => "Да, в субботу с 11 до 18";
        _ = after.CreateClient(); // запуск приложения
        Assert.Equal("Да, в субботу с 11 до 18", await after.Telegram.WaitFor(11));
    }
}
