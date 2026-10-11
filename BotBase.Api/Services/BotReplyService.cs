using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DbBusiness = BotBase.Api.Data.Entities.Business;

namespace BotBase.Api.Services;

// Формирует ответ бота клиенту и создаёт запись по маркеру [[BOOK:...]].
// Вызывается фоновым обработчиком BotWorker, а не прямо из webhook.
public class BotReplyService(
    AppDbContext db,
    TelegramService telegram,
    AiGateway ai,
    KnowledgeRetriever knowledge,
    CrmWebhookService crmWebhook)
{
    public async Task<string> GenerateReplyAsync(DbBusiness business, Guid conversationId, Func<Task>? onBusy, CancellationToken ct)
    {
        var businessId = business.Id;
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // В промпт идут только куски базы знаний, относящиеся к последним вопросам клиента (было: вся база, ~65k токенов).
        var query = string.Join(" ", messages.Where(m => m.Role == "user").TakeLast(3).Select(m => m.Content));
        var knowledgeText = knowledge.Select(business.KnowledgeChunks, query);

        // Load procedures and schedule to give AI booking context
        var procedures = await db.Procedures
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var schedule = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DayOfWeek)
            .ToListAsync();

        // Занятые слоты на 60 дней вперёд (раньше было 7 — ИИ не видел записи через неделю и обещал занятое время).
        // ScheduledAt хранит время по часам бизнеса (см. ToWallClockUtc), поэтому выводим его без пересчёта.
        var now = DateTime.UtcNow;
        var until = now.AddDays(60);
        var bookedSlots = (await db.Appointments
            .Where(a => a.BusinessId == businessId
                     && a.ScheduledAt >= now
                     && a.ScheduledAt <= until
                     && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new { a.ScheduledAt, a.ProcedureName, a.DurationMinutes })
            .ToListAsync())
            .Select(a => $"{a.ScheduledAt:dd.MM.yyyy HH:mm}–{a.ScheduledAt.AddMinutes(a.DurationMinutes):HH:mm} — {a.ProcedureName}")
            .ToList();

        var procedureList = procedures.Count == 0
            ? "Процедуры не настроены."
            : string.Join("\n", procedures.Select(p =>
                $"- {p.Name}: {p.DurationMinutes} мин, {p.Price:F0} руб."));

        var dayNames = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var scheduleText = string.Join("\n", schedule.Select(s =>
            s.IsWorkingDay
                ? $"- {dayNames[s.DayOfWeek]}: {s.StartTime:hh\\:mm}–{s.EndTime:hh\\:mm}"
                : $"- {dayNames[s.DayOfWeek]}: выходной"));

        var bookedText = bookedSlots.Count == 0
            ? "Свободно."
            : string.Join("\n", bookedSlots.Select(b => $"- {b}"));

        var today = DateTime.UtcNow;
        var todayStr = today.ToString("dd.MM.yyyy");
        var exampleDate = today.AddDays(1).ToString("yyyy-MM-dd");

        var systemPrompt = $$"""
            Ты ассистент компании "{{business.BusinessName}}".
            Отвечай только на вопросы, связанные с бизнесом. Будь вежлив и конкретен.

            Сегодняшняя дата: {{todayStr}}. Используй её при работе с датами записей.

            База знаний:
            {{knowledgeText}}

            ДОСТУПНЫЕ ПРОЦЕДУРЫ:
            {{procedureList}}

            РАБОЧЕЕ РАСПИСАНИЕ:
            {{scheduleText}}

            ЗАНЯТЫЕ СЛОТЫ (ближайшие 60 дней, на это время записывать НЕЛЬЗЯ):
            {{bookedText}}

            ИНСТРУКЦИЯ ПО ЗАПИСИ:
            Если клиент хочет записаться:
            1. Уточни желаемую процедуру (только из списка выше).
            2. Уточни дату и время (должны входить в рабочее расписание и не конфликтовать с занятыми слотами).
            3. Уточни имя клиента.
            4. Уточни номер телефона клиента.
            5. Когда ВСЕ 4 пункта собраны — подтверди запись клиенту и в конце ответа добавь РОВНО ОДНУ строку:
            [[BOOK:{"procedure_name":"...","scheduled_at":"{{exampleDate}}T11:00:00","duration_minutes":60,"client_name":"...","client_phone":"..."}]]
            Используй формат даты ISO 8601. Не добавляй [[BOOK:...]] пока не собраны все данные.
            """;

        var history = messages.Select(m => (m.Role, m.Content)).ToList();

        var rawReply = await ai.CompleteAsync(systemPrompt, history, onBusy, ct);

        // Parse [[BOOK:...]] marker and create appointment if found
        var booking = ExtractBookingMarker(rawReply);
        if (booking is not null)
        {
            var cleanReply = rawReply.Replace(booking.Value.FullMatch, "").Trim();
            // Сервер — последняя инстанция: если запись не создана, ответ ИИ «вы записаны» клиенту не уходит.
            var rejection = await TryCreateAppointmentAsync(businessId, conversationId, booking.Value.Json);
            return rejection ?? cleanReply;
        }

        return rawReply;
    }

    // Extracts [[BOOK:{json}]] from AI response
    private static (string Json, string FullMatch)? ExtractBookingMarker(string text)
    {
        const string prefix = "[[BOOK:";
        const string suffix = "]]";
        var start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.IndexOf(suffix, start + prefix.Length, StringComparison.Ordinal);
        if (end < 0) return null;
        var json      = text.Substring(start + prefix.Length, end - start - prefix.Length);
        var fullMatch = text.Substring(start, end - start + suffix.Length);
        return (json, fullMatch);
    }

    // Создаёт запись из маркера [[BOOK:...]]. Возвращает null, если запись создана,
    // иначе — честное сообщение клиенту (занято / нерабочее время / не хватает данных).
    private async Task<string?> TryCreateAppointmentAsync(Guid businessId, Guid conversationId, string json)
    {
        BookingData? data;
        try
        {
            data = JsonSerializer.Deserialize<BookingData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            data = null;
        }

        if (data is null ||
            string.IsNullOrWhiteSpace(data.procedure_name) ||
            string.IsNullOrWhiteSpace(data.client_name) ||
            string.IsNullOrWhiteSpace(data.client_phone) ||
            string.IsNullOrWhiteSpace(data.scheduled_at) ||
            data.duration_minutes <= 0 ||
            !DateTimeOffset.TryParse(data.scheduled_at, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return "Не получилось оформить запись: уточните, пожалуйста, процедуру, дату и время, имя и телефон.";
        }

        // Время по часам бизнеса — так, как его назвал клиент (смещение часового пояса игнорируем).
        var wallClock = parsed.DateTime;
        var scheduledAt = ToWallClockUtc(wallClock);
        var apptEnd = scheduledAt.AddMinutes(data.duration_minutes);

        var isoDay = ((int)wallClock.DayOfWeek + 6) % 7; // БД: 0=Пн..6=Вс
        var daySchedule = await db.WorkSchedules
            .FirstOrDefaultAsync(s => s.BusinessId == businessId && s.DayOfWeek == isoDay);
        if (daySchedule is not { IsWorkingDay: true, StartTime: { } open, EndTime: { } close })
            return $"К сожалению, {wallClock:dd.MM} у нас выходной. Подскажите, пожалуйста, другой день.";

        var start = TimeOnly.FromDateTime(wallClock);
        if (start < open || start.AddMinutes(data.duration_minutes) > close || start.AddMinutes(data.duration_minutes) < start)
            return $"К сожалению, {wallClock:dd.MM} мы работаем с {open:HH\\:mm} до {close:HH\\:mm}, " +
                   $"а процедура длится {data.duration_minutes} мин. Выберите, пожалуйста, время в эти часы.";

        if (scheduledAt < ToWallClockUtc(DateTime.UtcNow))
            return "Это время уже прошло. Подскажите, пожалуйста, другую дату или время.";

        Appointment appt;
        // Блокировка на бизнес: проверка «свободно?» и сохранение идут атомарно,
        // иначе два одновременных сообщения оба проходят проверку (гонка).
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockKey(businessId)})");

            var dayStart = ToWallClockUtc(wallClock.Date);
            var dayAppointments = await db.Appointments
                .Where(a => a.BusinessId == businessId
                         && a.Status != AppointmentStatus.Cancelled
                         && a.ScheduledAt >= dayStart.AddDays(-1)
                         && a.ScheduledAt < dayStart.AddDays(1))
                .Select(a => new { a.ScheduledAt, a.DurationMinutes })
                .ToListAsync();

            bool Busy(DateTime from, DateTime to) =>
                dayAppointments.Any(a => a.ScheduledAt < to && a.ScheduledAt.AddMinutes(a.DurationMinutes) > from);

            if (Busy(scheduledAt, apptEnd))
            {
                var free = new List<string>();
                for (var t = open; t.AddMinutes(data.duration_minutes) <= close && t.AddMinutes(data.duration_minutes) > t; t = t.AddMinutes(30))
                {
                    var from = dayStart.Add(t.ToTimeSpan());
                    if (from >= ToWallClockUtc(DateTime.UtcNow) && !Busy(from, from.AddMinutes(data.duration_minutes)))
                        free.Add(t.ToString("HH:mm"));
                }
                var nearest = free
                    .OrderBy(f => Math.Abs((TimeOnly.Parse(f) - start).TotalMinutes))
                    .Take(4).OrderBy(f => f).ToList();
                return nearest.Count == 0
                    ? $"К сожалению, {wallClock:dd.MM} в {wallClock:HH:mm} уже занято, и свободного времени в этот день не осталось. Подскажите, пожалуйста, другой день."
                    : $"К сожалению, {wallClock:dd.MM} в {wallClock:HH:mm} уже занято. Свободное время в этот день: {string.Join(", ", nearest)}. Какое вам подходит?";
            }

            appt = new Appointment
            {
                Id              = Guid.NewGuid(),
                BusinessId      = businessId,
                ConversationId  = conversationId,
                ProcedureName   = data.procedure_name,
                ClientName      = data.client_name,
                ClientPhone     = data.client_phone,
                ScheduledAt     = scheduledAt,
                DurationMinutes = data.duration_minutes,
                Status          = AppointmentStatus.Pending
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        try
        {
            var business = await db.Businesses.FindAsync(businessId);

            if (business?.CrmWebhookUrl is { } url)
                await crmWebhook.FireAsync(url, "appointment.created", appt);

            if (business?.OwnerNotificationChatId is { } ownerId && business.BotToken is { } botToken)
            {
                var msg = $"📅 Новая запись!\n" +
                          $"Клиент: {appt.ClientName}\n" +
                          $"Телефон: {appt.ClientPhone}\n" +
                          $"Процедура: {appt.ProcedureName}\n" +
                          $"Дата и время: {wallClock:dd.MM.yyyy HH:mm}\n" +
                          $"Длительность: {appt.DurationMinutes} мин";
                await telegram.SendMessageAsync(botToken, ownerId, msg);
            }
        }
        catch
        {
            // Запись уже сохранена; сбой уведомления не должен ломать ответ клиенту.
        }
        return null;
    }

    // Соглашение BotBase: ScheduledAt хранит время по часам бизнеса, помеченное как UTC
    // (как было на Railway с самого начала). Никаких пересчётов часовых поясов — ни на сервере, ни в браузере.
    private static DateTime ToWallClockUtc(DateTime wallClock) => DateTime.SpecifyKind(wallClock, DateTimeKind.Utc);

    private static long LockKey(Guid businessId) => BitConverter.ToInt64(businessId.ToByteArray(), 0);

    private record BookingData(
        string procedure_name,
        string scheduled_at,
        int duration_minutes,
        string client_name,
        string client_phone);
}
