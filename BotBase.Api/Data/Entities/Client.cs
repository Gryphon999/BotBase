namespace BotBase.Api.Data.Entities;

// Карточка клиента бизнеса. Один клиент = один номер телефона (PhoneKey — только цифры номера).
public class Client
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PhoneKey { get; set; } = "";
    // Чат клиента с ботом — через него приходят напоминания, просьба об отзыве и «давно не были».
    public long? TelegramChatId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? WinbackSentAt { get; set; }

    public Business Business { get; set; } = null!;
    public List<Appointment> Appointments { get; set; } = [];

    public static string KeyOf(string phone) => new(phone.Where(char.IsDigit).ToArray());
}
