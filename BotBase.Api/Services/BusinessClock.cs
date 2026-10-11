namespace BotBase.Api.Services;

// Соглашение BotBase: время записей хранится «по часам бизнеса» с пометкой Kind=Utc (без пересчёта поясов).
// Значит и «сейчас» нужно брать по часам бизнеса — иначе в Ереване (UTC+4) сервер ошибается на 4 часа.
public static class BusinessClock
{
    public const string DefaultZone = "Asia/Yerevan";

    public static TimeZoneInfo Zone(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? DefaultZone : id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(DefaultZone); }
    }

    public static bool IsValidZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception) { return false; }
    }

    // «Сейчас» по часам бизнеса в формате хранения записей.
    public static DateTime Now(string? zone) =>
        AsStored(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone(zone)));

    public static DateTime AsStored(DateTime wallClock) => DateTime.SpecifyKind(wallClock, DateTimeKind.Utc);
}
