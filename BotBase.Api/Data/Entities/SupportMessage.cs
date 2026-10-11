namespace BotBase.Api.Data.Entities;

public class SupportMessage
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public bool FromAdmin { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SupportTicket Ticket { get; set; } = null!;
}
