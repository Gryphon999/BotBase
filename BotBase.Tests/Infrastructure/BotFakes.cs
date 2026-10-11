using BotBase.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace BotBase.Tests.Infrastructure;

// ИИ по сценарию теста: отвечает функцией Respond и запоминает системные промпты.
public class FakeAi() : AnthropicService(null!, null!, null!, null!)
{
    public Func<string, List<(string role, string content)>, string> Respond { get; set; } = (_, _) => "Здравствуйте!";
    public ConcurrentBag<string> Prompts { get; } = [];

    public override Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history)
    {
        Prompts.Add(systemPrompt);
        return Task.FromResult(Respond(systemPrompt, history));
    }
}

// Telegram без сети: запоминает, что и кому бот отправил.
public class FakeTelegram() : TelegramService(null!)
{
    public ConcurrentQueue<(long ChatId, string Text)> Sent { get; } = new();

    public override Task<string?> GetBotUsernameAsync(string token) => Task.FromResult<string?>("test_bot");
    public override Task<bool> SetWebhookAsync(string token, string webhookUrl) => Task.FromResult(true);
    public override Task SendMessageAsync(string token, long chatId, string text)
    {
        Sent.Enqueue((chatId, text));
        return Task.CompletedTask;
    }

    public List<string> To(long chatId) => Sent.Where(s => s.ChatId == chatId).Select(s => s.Text).ToList();
}

// Приложение с подменёнными ИИ и Telegram — чтобы гонять настоящий webhook-сценарий записи.
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
