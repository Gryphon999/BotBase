namespace BotBase.Api.Models.Bots;

public record BotStatusResponse(bool HasBot, string? BotUsername, bool IsActive, bool OwnerConnected);
