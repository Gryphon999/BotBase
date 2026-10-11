using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace BotBase.Api.Services;

// Очередь чатов, которым нужно ответить. Серия сообщений от одного клиента склеивается:
// ответ формируется через DebounceMs после последнего сообщения (но не позже MaxDebounceMs после первого).
public class ChatQueue(IOptions<BotOptions> options, TimeProvider time)
{
    readonly ConcurrentDictionary<(Guid BusinessId, long ChatId), (DateTimeOffset First, DateTimeOffset Due)> _pending = new();
    readonly SemaphoreSlim _signal = new(0);

    public void Enqueue(Guid businessId, long chatId)
    {
        var now = time.GetUtcNow();
        var o = options.Value;
        _pending.AddOrUpdate((businessId, chatId),
            _ => (now, now.AddMilliseconds(o.DebounceMs)),
            (_, cur) =>
            {
                var due = now.AddMilliseconds(o.DebounceMs);
                var cap = cur.First.AddMilliseconds(o.MaxDebounceMs);
                return (cur.First, due < cap ? due : cap);
            });
        _signal.Release();
    }

    // Чаты, для которых пора отвечать; они удаляются из очереди.
    public List<(Guid BusinessId, long ChatId)> TakeDue()
    {
        var now = time.GetUtcNow();
        var due = new List<(Guid, long)>();
        foreach (var (key, value) in _pending)
            if (value.Due <= now && _pending.TryRemove(new KeyValuePair<(Guid, long), (DateTimeOffset, DateTimeOffset)>(key, value)))
                due.Add(key);
        return due;
    }

    public async Task WaitAsync(TimeSpan max, CancellationToken ct)
    {
        try { await _signal.WaitAsync(max, ct); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    public int Count => _pending.Count;
}
