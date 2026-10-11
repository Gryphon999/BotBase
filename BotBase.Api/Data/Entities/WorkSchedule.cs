namespace BotBase.Api.Data.Entities;

public class WorkSchedule
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    // null — общее расписание бизнеса; иначе личное расписание мастера.
    public Guid? MasterId { get; set; }
    public int DayOfWeek { get; set; }  // 0=Monday, 6=Sunday
    public bool IsWorkingDay { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public Business Business { get; set; } = null!;
}
