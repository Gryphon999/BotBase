namespace BotBase.Api.Data.Entities;

// Сотрудник бизнеса. Если у бизнеса нет ни одного мастера, всё работает «по-старому»:
// одно общее расписание, и в одно время возможна только одна запись.
public class Master
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    // Пустой список — мастер делает все процедуры бизнеса.
    public List<MasterProcedure> Procedures { get; set; } = [];
}

public class MasterProcedure
{
    public Guid MasterId { get; set; }
    public Guid ProcedureId { get; set; }

    public Master Master { get; set; } = null!;
    public Procedure Procedure { get; set; } = null!;
}
