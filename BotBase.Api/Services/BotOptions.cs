namespace BotBase.Api.Services;

// Настройки обработки сообщений бота (секция "Bot" в конфигурации, переменные Bot__*).
// Лимиты по умолчанию — чуть ниже бесплатного тарифа Gemini Flash (10 RPM, 250k TPM), чтобы не ловить 429.
public class BotOptions
{
    public int DebounceMs { get; set; } = 2500;          // ждём, не допишет ли клиент ещё сообщение
    public int MaxDebounceMs { get; set; } = 8000;       // но не дольше этого с первого сообщения серии
    public int RequestsPerMinute { get; set; } = 9;
    public int TokensPerMinute { get; set; } = 230_000;
    public int BusyNoticeAfterSec { get; set; } = 25;    // если ИИ занят дольше — пишем «секунду, много обращений»
    public int GiveUpAfterSec { get; set; } = 300;       // общий предел ожидания ответа ИИ
    public int KnowledgeBudgetChars { get; set; } = 12_000; // сколько текста базы знаний отправлять в ИИ
    public int MaxParallelChats { get; set; } = 8;

    // Автосообщения клиентам (напоминания, отзывы, «давно не были»).
    public int AutomationIntervalSec { get; set; } = 60;
    public int QuietHoursEnd { get; set; } = 9;     // не пишем клиентам раньше 9:00 по времени бизнеса
    public int QuietHoursStart { get; set; } = 21;  // и позже 21:00
}
