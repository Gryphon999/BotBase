using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Services;

public record BookingRequest(
    string ProcedureName,
    string ClientName,
    string ClientPhone,
    DateTime WallClock,
    AppointmentSource Source,
    int? DurationMinutes = null,
    Guid? ProcedureId = null,
    Guid? MasterId = null,
    string? MasterName = null,
    Guid? ConversationId = null,
    long? TelegramChatId = null,
    bool IgnoreWorkingHours = false);

// Error != null — запись не создана, Error можно показать клиенту/владельцу; FreeTimes — ближайшее свободное время в тот день.
public record BookingResult(Appointment? Appointment, string? Error, IReadOnlyList<string> FreeTimes)
{
    public static BookingResult Fail(string error, IReadOnlyList<string>? free = null) => new(null, error, free ?? []);
}

// Единая проверка «можно ли записать» для бота, ручной записи из кабинета и переноса.
// Учитывает рабочее время (общее или личное расписание мастера), занятость и мастеров.
public class BookingService(AppDbContext db)
{
    const int SlotStepMinutes = 30;

    public async Task<BookingResult> BookAsync(Guid businessId, BookingRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ClientName) || string.IsNullOrWhiteSpace(req.ClientPhone))
            return BookingResult.Fail("Укажите имя и телефон клиента.");

        var ctx = await LoadAsync(businessId);
        var procedure = req.ProcedureId is { } pid
            ? ctx.Procedures.FirstOrDefault(p => p.Id == pid)
            : MatchProcedure(ctx.Procedures, req.ProcedureName);
        if (req.ProcedureId is not null && procedure is null)
            return BookingResult.Fail("Процедура не найдена.");

        var duration = req.DurationMinutes is > 0 ? req.DurationMinutes.Value : procedure?.DurationMinutes ?? 0;
        if (duration <= 0) return BookingResult.Fail("Укажите длительность процедуры.");

        var candidates = Candidates(ctx, procedure, req.MasterId, req.MasterName, out var masterError);
        if (masterError is not null) return BookingResult.Fail(masterError);

        Appointment appt;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await LockAsync(businessId);
            var check = await FindFreeMasterAsync(ctx, candidates, req.WallClock, duration, req.IgnoreWorkingHours, req.Source, excludeId: null);
            if (check.Error is not null) return BookingResult.Fail(check.Error, check.Free);

            var client = await UpsertClientAsync(businessId, req.ClientName, req.ClientPhone, req.TelegramChatId);
            appt = new Appointment
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                ConversationId = req.ConversationId,
                ClientId = client.Id,
                MasterId = check.Master?.Id,
                ProcedureName = procedure?.Name ?? req.ProcedureName.Trim(),
                ClientName = req.ClientName.Trim(),
                ClientPhone = req.ClientPhone.Trim(),
                ScheduledAt = BusinessClock.AsStored(req.WallClock),
                DurationMinutes = duration,
                Price = procedure?.Price,
                Source = req.Source,
                // Запись, которую внёс сам владелец, уже подтверждена.
                Status = req.Source == AppointmentSource.Manual ? AppointmentStatus.Confirmed : AppointmentStatus.Pending
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        appt.Master = ctx.Masters.FirstOrDefault(m => m.Id == appt.MasterId);
        return new BookingResult(appt, null, []);
    }

    public async Task<BookingResult> RescheduleAsync(Appointment appt, DateTime newWallClock, Guid? masterId, bool ignoreWorkingHours)
    {
        var ctx = await LoadAsync(appt.BusinessId);
        var procedure = MatchProcedure(ctx.Procedures, appt.ProcedureName);
        var candidates = Candidates(ctx, procedure, masterId ?? appt.MasterId, null, out var masterError);
        if (masterError is not null) return BookingResult.Fail(masterError);

        await using var tx = await db.Database.BeginTransactionAsync();
        await LockAsync(appt.BusinessId);
        var check = await FindFreeMasterAsync(ctx, candidates, newWallClock, appt.DurationMinutes, ignoreWorkingHours, AppointmentSource.Manual, excludeId: appt.Id);
        if (check.Error is not null) return BookingResult.Fail(check.Error, check.Free);

        appt.ScheduledAt = BusinessClock.AsStored(newWallClock);
        appt.MasterId = check.Master?.Id;
        appt.UpdatedAt = DateTime.UtcNow;
        // Новое время — новое напоминание и новое подтверждение от клиента.
        appt.ReminderSentAt = null;
        appt.ClientConfirmedAt = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        appt.Master = ctx.Masters.FirstOrDefault(m => m.Id == appt.MasterId);
        return new BookingResult(appt, null, []);
    }

    // Клиент с этим номером (по цифрам) — существующий или новый. Telegram-чат запоминаем, чтобы слать напоминания.
    public async Task<Client> UpsertClientAsync(Guid businessId, string name, string phone, long? telegramChatId)
    {
        var key = Client.KeyOf(phone);
        var client = key.Length == 0 ? null
            : await db.Clients.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.PhoneKey == key);
        if (client is null)
        {
            client = new Client { Id = Guid.NewGuid(), BusinessId = businessId, Name = name.Trim(), Phone = phone.Trim(), PhoneKey = key };
            db.Clients.Add(client);
        }
        if (telegramChatId is not null) client.TelegramChatId = telegramChatId;
        if (string.IsNullOrWhiteSpace(client.Name)) client.Name = name.Trim();
        await db.SaveChangesAsync();
        return client;
    }

    // ---------- внутреннее ----------

    record Context(Business Business, List<Procedure> Procedures, List<Master> Masters, List<WorkSchedule> Schedules);
    record Check(Master? Master, string? Error, IReadOnlyList<string> Free);

    async Task<Context> LoadAsync(Guid businessId)
    {
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId);
        var procedures = await db.Procedures.AsNoTracking().Where(p => p.BusinessId == businessId && p.IsActive).ToListAsync();
        var masters = await db.Masters.AsNoTracking().Include(m => m.Procedures)
            .Where(m => m.BusinessId == businessId && m.IsActive).OrderBy(m => m.CreatedAt).ToListAsync();
        var schedules = await db.WorkSchedules.AsNoTracking().Where(s => s.BusinessId == businessId).ToListAsync();
        return new Context(business, procedures, masters, schedules);
    }

    // ИИ называет процедуру своими словами: сначала точное совпадение, потом «одно содержит другое».
    public static Procedure? MatchProcedure(IReadOnlyCollection<Procedure> procedures, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        return procedures.FirstOrDefault(p => string.Equals(p.Name.Trim(), n, StringComparison.OrdinalIgnoreCase))
            ?? procedures.Where(p => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase) || n.Contains(p.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(p => p.Name.Length).FirstOrDefault();
    }

    public static bool CanDo(Master m, Procedure? p) => p is null || m.Procedures.Count == 0 || m.Procedures.Any(mp => mp.ProcedureId == p.Id);

    // Кто может принять запись. [null] — у бизнеса нет мастеров (одно общее расписание).
    static List<Master?> Candidates(Context ctx, Procedure? procedure, Guid? masterId, string? masterName, out string? error)
    {
        error = null;
        if (ctx.Masters.Count == 0) return [null];

        var capable = ctx.Masters.Where(m => CanDo(m, procedure)).ToList();
        if (masterId is { } id)
        {
            var m = ctx.Masters.FirstOrDefault(x => x.Id == id);
            if (m is null) { error = "Мастер не найден."; return []; }
            if (!CanDo(m, procedure)) { error = $"{m.Name} не делает процедуру «{procedure!.Name}»."; return []; }
            return [m];
        }
        if (!string.IsNullOrWhiteSpace(masterName))
        {
            // Клиент назвал мастера — пробуем его; если имя не узнали, подойдёт любой свободный.
            var named = capable.FirstOrDefault(m => m.Name.Contains(masterName.Trim(), StringComparison.OrdinalIgnoreCase)
                                                 || masterName.Contains(m.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (named is not null) return [named];
        }
        if (capable.Count == 0) { error = $"Сейчас нет мастера, который делает «{procedure?.Name}»."; return []; }
        return capable.Cast<Master?>().ToList();
    }

    // Рабочее время в этот день: личное расписание мастера, если оно задано, иначе общее.
    static (TimeOnly Open, TimeOnly Close)? Hours(Context ctx, Master? master, DateTime day)
    {
        var isoDay = ((int)day.DayOfWeek + 6) % 7;
        var own = master is null ? [] : ctx.Schedules.Where(s => s.MasterId == master.Id).ToList();
        var rows = own.Count > 0 ? own : ctx.Schedules.Where(s => s.MasterId == null).ToList();
        var row = rows.FirstOrDefault(s => s.DayOfWeek == isoDay);
        return row is { IsWorkingDay: true, StartTime: { } open, EndTime: { } close } ? (open, close) : null;
    }

    async Task<Check> FindFreeMasterAsync(Context ctx, List<Master?> candidates, DateTime wallClock, int duration,
        bool ignoreWorkingHours, AppointmentSource source, Guid? excludeId)
    {
        var start = BusinessClock.AsStored(wallClock);
        var end = start.AddMinutes(duration);
        var now = BusinessClock.Now(ctx.Business.TimeZone);
        // Владелец может внести прошедший визит задним числом; клиенту бота записаться в прошлое нельзя.
        if (source != AppointmentSource.Manual && start < now)
            return new Check(null, "Это время уже прошло. Подскажите, пожалуйста, другую дату или время.", []);

        var dayStart = BusinessClock.AsStored(wallClock.Date);
        var busy = await db.Appointments.AsNoTracking()
            .Where(a => a.BusinessId == ctx.Business.Id && a.Status != AppointmentStatus.Cancelled
                     && a.ScheduledAt >= dayStart.AddDays(-1) && a.ScheduledAt < dayStart.AddDays(2)
                     && (excludeId == null || a.Id != excludeId))
            .Select(a => new { a.ScheduledAt, a.DurationMinutes, a.MasterId })
            .ToListAsync();

        // Запись без мастера (сделана до появления мастеров) занимает всех — так безопаснее.
        bool Busy(Master? m, DateTime from, DateTime to) => busy.Any(a =>
            (m is null || a.MasterId is null || a.MasterId == m.Id)
            && a.ScheduledAt < to && a.ScheduledAt.AddMinutes(a.DurationMinutes) > from);

        bool InHours(Master? m, DateTime from, DateTime to)
        {
            if (ignoreWorkingHours) return true;
            if (Hours(ctx, m, from) is not { } h) return false;
            var s = TimeOnly.FromDateTime(from);
            return to.Date == from.Date && s >= h.Open && TimeOnly.FromDateTime(to) <= h.Close && to > from;
        }

        foreach (var m in candidates)
            if (InHours(m, start, end) && !Busy(m, start, end))
                return new Check(m, null, []);

        // Почему нельзя: выходной / вне часов / занято — и что можно предложить взамен.
        var free = new SortedSet<string>();
        foreach (var m in candidates)
        {
            if (Hours(ctx, m, wallClock) is not { } h) continue;
            for (var t = h.Open; t.AddMinutes(duration) <= h.Close && t.AddMinutes(duration) > t; t = t.AddMinutes(SlotStepMinutes))
            {
                var from = dayStart.Add(t.ToTimeSpan());
                if ((source == AppointmentSource.Manual || from >= now) && !Busy(m, from, from.AddMinutes(duration)))
                    free.Add(t.ToString("HH:mm"));
            }
        }
        var wanted = TimeOnly.FromDateTime(wallClock);
        var nearest = free.OrderBy(f => Math.Abs((TimeOnly.Parse(f) - wanted).TotalMinutes)).Take(4).OrderBy(f => f).ToList();

        if (!ignoreWorkingHours && candidates.All(m => Hours(ctx, m, wallClock) is null))
            return new Check(null, $"К сожалению, {wallClock:dd.MM} у нас выходной. Подскажите, пожалуйста, другой день.", []);

        if (!ignoreWorkingHours && candidates.All(m => !InHours(m, start, end)))
        {
            var h = candidates.Select(m => Hours(ctx, m, wallClock)).First(x => x is not null)!.Value;
            return new Check(null, $"К сожалению, {wallClock:dd.MM} мы работаем с {h.Open:HH\\:mm} до {h.Close:HH\\:mm}, " +
                                   $"а процедура длится {duration} мин. Выберите, пожалуйста, время в эти часы.", nearest);
        }

        return nearest.Count == 0
            ? new Check(null, $"К сожалению, {wallClock:dd.MM} в {wallClock:HH:mm} уже занято, и свободного времени в этот день не осталось. Подскажите, пожалуйста, другой день.", [])
            : new Check(null, $"К сожалению, {wallClock:dd.MM} в {wallClock:HH:mm} уже занято. Свободное время в этот день: {string.Join(", ", nearest)}. Какое вам подходит?", nearest);
    }

    // Блокировка на бизнес: проверка «свободно?» и сохранение идут атомарно (иначе два одновременных сообщения проходят оба).
    Task LockAsync(Guid businessId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockKey(businessId)})");

    static long LockKey(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
