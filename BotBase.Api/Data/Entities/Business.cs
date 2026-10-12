namespace BotBase.Api.Data.Entities;

public class Business
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public string? BotToken { get; set; }
    public string? CrmWebhookUrl { get; set; }
    public long? OwnerNotificationChatId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Когда владелец принял условия и политику конфиденциальности (галочка при регистрации). null — аккаунт старше галочки.
    public DateTime? TermsAcceptedAt { get; set; }

    // Сброс пароля по ссылке из письма: храним только SHA-256 токена и срок действия.
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresAt { get; set; }

    // Часовой пояс бизнеса: время записей хранится «по часам бизнеса», а «сейчас» считаем в этом поясе.
    public string TimeZone { get; set; } = "Asia/Yerevan";

    // Автосообщения клиентам (страница «Автосообщения»).
    public bool RemindersEnabled { get; set; } = true;
    public bool ReviewRequestsEnabled { get; set; }
    public string? ReviewUrl { get; set; }
    public bool WinbackEnabled { get; set; }
    public int WinbackAfterDays { get; set; } = 45;
    // Язык автосообщений, если клиент ещё ничего не писал боту (иначе — язык его сообщений): ru, hy, en.
    public string MessagesLanguage { get; set; } = "ru";

    // Тариф «Бизнес» (CRM-раздел). Открывает и закрывает админ; CrmUntil — «оплачено до» (включительно,
    // по часам бизнеса), после этой даты доступ закрывается сам. null — бессрочно.
    public bool CrmEnabled { get; set; }
    public DateTime? CrmUntil { get; set; }

    public bool HasCrm(DateTime nowLocal) => CrmEnabled && (CrmUntil is null || nowLocal.Date <= CrmUntil.Value.Date);

    public List<KnowledgeChunk> KnowledgeChunks { get; set; } = [];
    public List<Conversation> Conversations { get; set; } = [];
}
