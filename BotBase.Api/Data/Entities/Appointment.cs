namespace BotBase.Api.Data.Entities;

public enum AppointmentStatus { Pending, Confirmed, Cancelled }

public class Appointment
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? ConversationId { get; set; }
    public string ProcedureName { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
