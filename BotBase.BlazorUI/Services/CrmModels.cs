namespace BotBase.BlazorUI.Services;

// Модели CRM-раздела кабинета (записи, клиенты, мастера, статистика, автосообщения).
public record AppointmentItem(
    Guid Id, string ProcedureName, string ClientName, string ClientPhone,
    DateTime ScheduledAt, int DurationMinutes, string Status,
    DateTime CreatedAt, DateTime UpdatedAt,
    Guid? MasterId, string? MasterName, Guid? ClientId, string Source, decimal? Price,
    DateTime? ReminderSentAt, DateTime? ClientConfirmedAt, bool NoShow, int? Rating, bool HasTelegram)
{
    public DateTime EndsAt => ScheduledAt.AddMinutes(DurationMinutes);
    public bool IsCancelled => Status == "Cancelled";
}

public record ProcedureItem(Guid Id, string Name, int DurationMinutes, decimal Price);

public record ClientItem(Guid Id, string Name, string Phone, bool HasTelegram, int Visits,
    DateTime? LastVisit, DateTime? NextVisit, decimal TotalSpent, string? Notes);

public record ScheduleDay(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);

public record MasterItem(Guid Id, string Name, List<Guid> ProcedureIds, List<ScheduleDay>? Schedule);

public record AutomationSettings(string TimeZone, bool RemindersEnabled, bool ReviewRequestsEnabled, string? ReviewUrl, bool WinbackEnabled, int WinbackAfterDays, string MessagesLanguage = "ru");

public record StatsTop(string Name, int Count, decimal Revenue);
public record StatsDay(int Day, int Count);
public record StatsData(string Month, int Total, int ByBot, int ByManual, int ByIntegration, int Cancelled, int NoShows, int Upcoming,
    int Visits, decimal Revenue, decimal BotRevenue, decimal AverageCheck, int Clients, int NewClients, int ConfirmedByReminder,
    double? AverageRating, int RatingsCount, List<StatsTop> TopProcedures, List<StatsTop> ByMaster, List<StatsDay> ByDay);

// Ответ сервера «нельзя записать»: причина и ближайшее свободное время.
public record BookingProblem(string? Error, List<string>? FreeTimes);

public static class Fmt
{
    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");
    public static string Money(decimal value) => value.ToString("#,0", Ru);
    public static string Day(DateTime d) => d.ToString("d MMMM", Ru);
    public static string DayTime(DateTime d) => d.ToString("d MMM, HH:mm", Ru);
    public static string Weekday(int iso) => iso switch { 0 => "Понедельник", 1 => "Вторник", 2 => "Среда", 3 => "Четверг", 4 => "Пятница", 5 => "Суббота", _ => "Воскресенье" };
}
