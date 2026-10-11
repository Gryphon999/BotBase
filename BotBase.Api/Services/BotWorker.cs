using BotBase.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DbMessage = BotBase.Api.Data.Entities.Message;

namespace BotBase.Api.Services;

// Фоновый обработчик: берёт из ChatQueue чаты, где клиент закончил серию сообщений, и отвечает им.
// Клиент никогда не видит «сервис недоступен»: бот ждёт лимит ИИ, показывает «печатает…»,
// а при долгом ожидании честно пишет, что ответит чуть позже.
public class BotWorker(ChatQueue queue, IServiceScopeFactory scopes, IOptions<BotOptions> options, ILogger<BotWorker> log) : BackgroundService
{
    public const string BusyNotice = "Секунду, сейчас много обращений — отвечу через минуту 🙏";
    public const string GiveUpNotice = "Извините, не получилось ответить сразу. Напишите, пожалуйста, ещё раз через пару минут — мы обязательно ответим.";

    readonly HashSet<(Guid, long)> _inProgress = [];
    readonly Lock _lock = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverUnansweredAsync(ct);
        using var parallel = new SemaphoreSlim(options.Value.MaxParallelChats);

        while (!ct.IsCancellationRequested)
        {
            await queue.WaitAsync(TimeSpan.FromMilliseconds(200), ct);
            foreach (var chat in queue.TakeDue())
            {
                lock (_lock)
                {
                    // Чат уже обрабатывается — вернём его в очередь, ответим после текущего ответа.
                    if (!_inProgress.Add(chat)) { queue.Enqueue(chat.BusinessId, chat.ChatId); continue; }
                }
                await parallel.WaitAsync(ct);
                _ = Task.Run(async () =>
                {
                    try { await RespondAsync(chat.BusinessId, chat.ChatId, ct); }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogError(ex, "Reply failed for chat {Chat}", chat.ChatId); }
                    finally
                    {
                        lock (_lock) _inProgress.Remove(chat);
                        parallel.Release();
                    }
                }, ct);
            }
        }
    }

    async Task RespondAsync(Guid businessId, long chatId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var telegram = scope.ServiceProvider.GetRequiredService<TelegramService>();
        var replies = scope.ServiceProvider.GetRequiredService<BotReplyService>();

        var business = await db.Businesses.Include(b => b.KnowledgeChunks)
            .FirstOrDefaultAsync(b => b.Id == businessId && b.IsActive, ct);
        if (business?.BotToken is not { } token) return;
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.TelegramChatId == chatId, ct);
        if (conversation is null) return;

        // Отвечаем, только если последнее слово за клиентом (защита от двойного ответа).
        var last = await db.Messages.Where(m => m.ConversationId == conversation.Id).OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        if (last is null || last.Role != "user") return;

        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typing = Task.Run(async () =>
        {
            while (!typingCts.IsCancellationRequested)
            {
                await telegram.SendTypingAsync(token, chatId);
                try { await Task.Delay(4500, typingCts.Token); } catch (OperationCanceledException) { }
            }
        }, CancellationToken.None);

        string reply;
        try
        {
            reply = await replies.GenerateReplyAsync(business, conversation.Id,
                onBusy: () => telegram.SendMessageAsync(token, chatId, BusyNotice), ct);
        }
        catch (AiUnavailableException ex)
        {
            log.LogError(ex, "AI unavailable for business {Business}", businessId);
            reply = GiveUpNotice;
        }
        finally
        {
            typingCts.Cancel();
            await typing;
        }

        db.Messages.Add(new DbMessage { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = reply });
        await db.SaveChangesAsync(ct);
        await telegram.SendMessageAsync(token, chatId, reply);
    }

    // Очередь живёт в памяти: если сервер перезапустился (деплой), подхватываем свежие сообщения без ответа.
    async Task RecoverUnansweredAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateTime.UtcNow.AddMinutes(-30);
            var pending = await db.Conversations
                .Where(c => c.Business.IsActive && c.Business.BotToken != null)
                .Select(c => new { c.BusinessId, c.TelegramChatId, Last = c.Messages.OrderByDescending(m => m.CreatedAt).Select(m => new { m.Role, m.CreatedAt }).FirstOrDefault() })
                .Where(x => x.Last != null && x.Last.Role == "user" && x.Last.CreatedAt > since)
                .ToListAsync(ct);
            foreach (var p in pending) queue.Enqueue(p.BusinessId, p.TelegramChatId);
            if (pending.Count > 0) log.LogInformation("Recovered {Count} unanswered chats after restart", pending.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Recovery of unanswered chats failed");
        }
    }
}
