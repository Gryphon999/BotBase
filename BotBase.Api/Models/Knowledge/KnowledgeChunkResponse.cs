namespace BotBase.Api.Models.Knowledge;

// Broken — текст файла в основном из «����» (сломанная кодировка), бот его не понимает.
// DuplicateOf — имя другого файла, который уже содержит весь этот текст.
public record KnowledgeChunkResponse(Guid Id, string FileName, DateTime UploadedAt, int TextLength, bool Broken = false, string? DuplicateOf = null);

public record KnowledgeChunkDetails(Guid Id, string FileName, DateTime UploadedAt, string Text, bool Broken);
