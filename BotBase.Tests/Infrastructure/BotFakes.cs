using BotBase.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace BotBase.Tests.Infrastructure;

// ИИ по сценарию теста: отвечает функцией Respond, запоминает промпты, умеет «падать» заданное число раз.
public class FakeAi() : AnthropicService(null!, null!, null!)
{
    public Func<string, List<(string role, string content)>, string> Respond { get; set; } = (_, _) => "Здравствуйте!";
    public ConcurrentBag<string> Prompts { get; } = [];
    public int Calls => _calls;
    public int FailNext { get; set; }                       // сколько ближайших вызовов ответят 429
    public bool AlwaysFail { get; set; }
    int _calls;

    public override Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history)
    {
        Interlocked.Increment(ref _calls);
        if (AlwaysFail) throw new AiRateLimitedException("fake 429");
        if (FailNext > 0) { FailNext--; throw new AiRateLimitedException("fake 429"); }
        Prompts.Add(systemPrompt);
        return Task.FromResult(Respond(systemPrompt, history));
    }
}

// Telegram без сети: запоминает, что и кому бот отправил.
public class FakeTelegram() : TelegramService(null!)
{
    public ConcurrentQueue<(long ChatId, string Text)> Sent { get; } = new();
    public int Typing => _typing;
    int _typing;

    public override Task<string?> GetBotUsernameAsync(string token) => Task.FromResult<string?>("test_bot");
    public override Task<bool> SetWebhookAsync(string token, string webhookUrl) => Task.FromResult(true);
    public override Task SendTypingAsync(string token, long chatId) { Interlocked.Increment(ref _typing); return Task.CompletedTask; }
    public override Task SendMessageAsync(string token, long chatId, string text)
    {
        Sent.Enqueue((chatId, text));
        return Task.CompletedTask;
    }

    public List<string> To(long chatId) => Sent.Where(s => s.ChatId == chatId).Select(s => s.Text).ToList();

    // Кнопки последних сообщений: (чат, текст сообщения, данные кнопок).
    public ConcurrentQueue<(long ChatId, string Text, List<string> Data)> Buttons { get; } = new();

    public override Task SendButtonsAsync(string token, long chatId, string text, IReadOnlyList<IReadOnlyList<(string Text, string Data)>> rows)
    {
        Sent.Enqueue((chatId, text));
        Buttons.Enqueue((chatId, text, rows.SelectMany(r => r).Select(b => b.Data).ToList()));
        return Task.CompletedTask;
    }

    public override Task AnswerCallbackAsync(string token, string callbackId, string? text = null) => Task.CompletedTask;
    public override Task RemoveButtonsAsync(string token, long chatId, int messageId) => Task.CompletedTask;

    // Ответ теперь приходит асинхронно (через очередь) — ждём нужное сообщение.
    public async Task<string> WaitFor(long chatId, Func<string, bool>? match = null, int timeoutMs = 15000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            var hit = To(chatId).LastOrDefault(m => match?.Invoke(m) ?? true);
            if (hit is not null) return hit;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Бот не ответил в чат {chatId}. Отправлено: {string.Join(" | ", Sent.Select(s => s.ChatId + ":" + s.Text))}");
    }
}

// Приложение с подменёнными ИИ и Telegram — чтобы гонять настоящий webhook-сценарий.
public class BotApiFactory(string connectionString) : ApiFactory(connectionString)
{
    public FakeAi Ai { get; } = new();
    public FakeTelegram Telegram { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<AnthropicService>(_ => Ai);
            services.AddScoped<TelegramService>(_ => Telegram);
        });
    }
}
