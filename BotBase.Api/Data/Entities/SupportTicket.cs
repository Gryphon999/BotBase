namespace BotBase.Api.Data.Entities;

public enum SupportTicketStatus { Open, Answered, Closed }

public class SupportTicket
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Subject { get; set; } = "";
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public List<SupportMessage> Messages { get; set; } = [];
}
