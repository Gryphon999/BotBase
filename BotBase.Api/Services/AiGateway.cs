using Microsoft.Extensions.Options;

namespace BotBase.Api.Services;

public class AiRateLimitedException(string message) : Exception(message);
public class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

// Единая точка доступа к ИИ для всей платформы: соблюдает лимиты запросов и токенов в минуту,
// при их нехватке ЖДЁТ (а не отвечает клиенту ошибкой), на 429/сбои — повторяет с паузой.
public class AiGateway(IServiceScopeFactory scopes, IOptions<BotOptions> options, ILogger<AiGateway> log)
{
    readonly Queue<(DateTime At, int Tokens)> _window = new();
    readonly Lock _lock = new();

    // Грубая оценка токенов: для русского текста ~3.5 символа на токен.
    public static int EstimateTokens(string systemPrompt, IEnumerable<(string role, string content)> history) =>
        (systemPrompt.Length + history.Sum(h => h.content.Length)) * 2 / 7 + 600;

    public async Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history, Func<Task>? onBusy, CancellationToken ct)
    {
        var o = options.Value;
        var started = DateTime.UtcNow;
        var deadline = started.AddSeconds(o.GiveUpAfterSec);
        var busyNotified = false;
        var tokens = EstimateTokens(systemPrompt, history);
        var attempt = 0;

        async Task NotifyBusyIfSlow()
        {
            if (!busyNotified && onBusy is not null && DateTime.UtcNow - started > TimeSpan.FromSeconds(o.BusyNoticeAfterSec))
            {
                busyNotified = true;
                try { await onBusy(); } catch (Exception ex) { log.LogWarning(ex, "Busy notice failed"); }
            }
        }

        while (true)
        {
            // 1) ждём место в лимитах
            while (!TryReserve(tokens, o))
            {
                if (DateTime.UtcNow > deadline) throw new AiUnavailableException("Лимит ИИ не освободился за отведённое время");
                await NotifyBusyIfSlow();
                await Task.Delay(500, ct);
            }

            // 2) запрос
            try
            {
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<AnthropicService>().CompleteAsync(systemPrompt, history);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                attempt++;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || (ex is not AiRateLimitedException && attempt >= 4))
                {
                    await NotifyBusyIfSlow();
                    throw new AiUnavailableException("ИИ не ответил", ex);
                }
                // Пауза растёт (2, 4, 8… до 30 с), но не дольше оставшегося времени ожидания.
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                if (delay > remaining) delay = remaining;
                log.LogWarning(ex, "AI request failed (attempt {Attempt}), retry in {Delay}s", attempt, delay.TotalSeconds);
                // Ждём кусками, чтобы вовремя сказать клиенту «секунду, много обращений».
                for (var waited = TimeSpan.Zero; waited < delay; waited += TimeSpan.FromMilliseconds(500))
                {
                    await NotifyBusyIfSlow();
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, (delay - waited).TotalMilliseconds)), ct);
                }
            }
        }
    }

    bool TryReserve(int tokens, BotOptions o)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (_window.Count > 0 && _window.Peek().At < cutoff) _window.Dequeue();
            var usedTokens = _window.Sum(w => w.Tokens);
            // Слишком большой запрос пропускаем, только когда окно пустое — иначе он никогда не пройдёт.
            if (_window.Count >= o.RequestsPerMinute || (usedTokens + tokens > o.TokensPerMinute && _window.Count > 0))
                return false;
            _window.Enqueue((DateTime.UtcNow, tokens));
            return true;
        }
    }
}
