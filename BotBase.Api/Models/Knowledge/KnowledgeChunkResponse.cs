namespace BotBase.Api.Models.Knowledge;

public record KnowledgeChunkResponse(Guid Id, string FileName, DateTime UploadedAt, int TextLength);
