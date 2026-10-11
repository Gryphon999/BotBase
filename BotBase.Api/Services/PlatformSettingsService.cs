using BotBase.Api.Data;
using BotBase.Api.Data.Entities;

namespace BotBase.Api.Services;

public class PlatformSettingsService(AppDbContext db)
{
    public const string SupportEmail = "SupportEmail";
    public const string SupportTelegramChatId = "SupportTelegramChatId";

    public async Task<string?> GetAsync(string key) =>
        (await db.PlatformSettings.FindAsync(key))?.Value is { Length: > 0 } v ? v : null;

    public async Task SetAsync(string key, string? value)
    {
        var s = await db.PlatformSettings.FindAsync(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (s is not null) db.PlatformSettings.Remove(s);
        }
        else if (s is null) db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value.Trim() });
        else s.Value = value.Trim();
        await db.SaveChangesAsync();
    }
}
