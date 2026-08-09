namespace BotBase.Api.Data.Entities;

public class KnowledgeChunk
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string FileName { get; set; } = "";
    public string ExtractedText { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
