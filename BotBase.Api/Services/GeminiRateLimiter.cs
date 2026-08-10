namespace BotBase.Api.Services;

public class GeminiRateLimiter
{
    private readonly Queue<DateTime> _requestTimes = new();
    private readonly Lock _lock = new();
    private const int MaxRequestsPerMinute = 14;

    public bool TryAcquire()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (_requestTimes.Count > 0 && _requestTimes.Peek() < cutoff)
                _requestTimes.Dequeue();

            if (_requestTimes.Count >= MaxRequestsPerMinute)
                return false;

            _requestTimes.Enqueue(DateTime.UtcNow);
            return true;
        }
    }
}
