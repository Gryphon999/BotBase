namespace BotBase.Api.Data.Entities;

// Статусы не расширяем: их получает CRM-интеграция (вебхук), новые значения могли бы её сломать.
public enum AppointmentStatus { Pending, Confirmed, Cancelled }

public enum AppointmentSource { Bot, Manual, Integration }

public class Appointment
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? MasterId { get; set; }
    public string ProcedureName { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    // Цена на момент записи — для статистики выручки (прайс потом может измениться).
    public decimal? Price { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public AppointmentSource Source { get; set; } = AppointmentSource.Bot;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReminderSentAt { get; set; }
    public DateTime? ClientConfirmedAt { get; set; }
    public bool NoShow { get; set; }
    public DateTime? ReviewRequestedAt { get; set; }
    public int? Rating { get; set; }

    public Business Business { get; set; } = null!;
    public Client? Client { get; set; }
    public Master? Master { get; set; }
}
