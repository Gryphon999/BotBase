namespace BotBase.Api.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public long TelegramChatId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    // Когда клиенту отправили уведомление об обработке данных со ссылкой на политику (один раз на чат).
    public DateTime? PrivacyNoticeAt { get; set; }

    public Business Business { get; set; } = null!;
    public List<Message> Messages { get; set; } = [];
}
