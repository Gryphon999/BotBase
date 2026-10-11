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
    CrmWebhookService crmWebhook,
    BookingService booking,
    OwnerMessages ownerMessages)
{
    static readonly string[] DayNames = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

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

        var procedures = await db.Procedures
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var schedules = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DayOfWeek)
            .ToListAsync(ct);

        var masters = !business.HasCrm(BusinessClock.Now(business.TimeZone)) ? [] : await db.Masters.Include(m => m.Procedures)
            .Where(m => m.BusinessId == businessId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Занятые слоты на 60 дней вперёд. ScheduledAt хранит время по часам бизнеса, поэтому выводим его без пересчёта.
        var now = BusinessClock.Now(business.TimeZone);
        var until = now.AddDays(60);
        var masterNames = masters.ToDictionary(m => m.Id, m => m.Name);
        var bookedSlots = (await db.Appointments
            .Where(a => a.BusinessId == businessId
                     && a.ScheduledAt >= now.Date
                     && a.ScheduledAt <= until
                     && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new { a.ScheduledAt, a.ProcedureName, a.DurationMinutes, a.MasterId })
            .ToListAsync(ct))
            .Select(a => $"{a.ScheduledAt:dd.MM.yyyy HH:mm}–{a.ScheduledAt.AddMinutes(a.DurationMinutes):HH:mm} — {a.ProcedureName}" +
                         (masters.Count > 0
                             ? a.MasterId is { } mid && masterNames.TryGetValue(mid, out var mn) ? $" (мастер {mn})" : " (все мастера)"
                             : ""))
            .ToList();

        // Валюту не пишем: у клиентов BotBase цены в драмах, рублях, долларах — валюта есть в базе знаний.
        var procedureList = procedures.Count == 0
            ? "Процедуры не настроены."
            : string.Join("\n", procedures.Select(p => $"- {p.Name}: {p.DurationMinutes} мин, цена {p.Price:0.##}"));

        var scheduleText = ScheduleText(schedules.Where(s => s.MasterId == null));

        var mastersText = masters.Count == 0 ? "" : $$"""

            МАСТЕРА (у каждого свои записи; в одно время разные мастера могут принимать разных клиентов):
            {{string.Join("\n", masters.Select(m =>
            {
                var does = m.Procedures.Count == 0
                    ? "все процедуры"
                    : string.Join(", ", procedures.Where(p => m.Procedures.Any(mp => mp.ProcedureId == p.Id)).Select(p => p.Name));
                var own = schedules.Where(s => s.MasterId == m.Id).ToList();
                var hours = own.Count == 0 ? "по общему расписанию" : "\n" + ScheduleText(own).Replace("- ", "    - ");
                return $"- {m.Name}: {does}; работает {hours}";
            }))}}
            """;

        var bookedText = bookedSlots.Count == 0
            ? "Свободно."
            : string.Join("\n", bookedSlots.Select(b => $"- {b}"));

        var todayStr = $"{now:dd.MM.yyyy} ({DayNames[((int)now.DayOfWeek + 6) % 7]}), сейчас {now:HH:mm}";
        var exampleDate = now.AddDays(1).ToString("yyyy-MM-dd");
        var masterStep = masters.Count == 0 ? "" :
            "\n            2а. Если клиент хочет к определённому мастеру — учти это; если нет — предложи любого свободного мастера, который делает эту процедуру.";
        var masterField = masters.Count == 0 ? "" : ",\"master_name\":\"...\"";

        var systemPrompt = $$"""
            Ты ассистент компании "{{business.BusinessName}}".
            Отвечай только на вопросы, связанные с бизнесом. Будь вежлив и конкретен.
            Отвечай на том языке, на котором пишет клиент (русский, армянский, английский и т.д.).
            Цены, адрес, условия и факты о процедурах бери ТОЛЬКО из базы знаний и списка процедур ниже — ничего не придумывай.
            Если нужной информации там нет, честно скажи, что не можешь ответить точно, и предложи связаться с администратором (контакты возьми из базы знаний, если они там есть).

            Сегодня: {{todayStr}}. Используй это при работе с датами записей.

            База знаний:
            {{knowledgeText}}

            ДОСТУПНЫЕ ПРОЦЕДУРЫ:
            {{procedureList}}

            РАБОЧЕЕ РАСПИСАНИЕ:
            {{scheduleText}}
            {{mastersText}}

            ЗАНЯТЫЕ СЛОТЫ (ближайшие 60 дней, на это время записывать НЕЛЬЗЯ):
            {{bookedText}}

            ИНСТРУКЦИЯ ПО ЗАПИСИ:
            Если клиент хочет записаться:
            1. Уточни желаемую процедуру (только из списка выше).
            2. Уточни дату и время (должны входить в рабочее расписание и не конфликтовать с занятыми слотами).{{masterStep}}
            3. Уточни имя клиента.
            4. Уточни номер телефона клиента.
            5. Когда ВСЕ 4 пункта собраны — подтверди запись клиенту и в конце ответа добавь РОВНО ОДНУ строку:
            [[BOOK:{"procedure_name":"...","scheduled_at":"{{exampleDate}}T11:00:00","duration_minutes":60,"client_name":"...","client_phone":"..."{{masterField}}}]]
            Используй формат даты ISO 8601. Не добавляй [[BOOK:...]] пока не собраны все данные.
            """;

        var history = messages.Select(m => (m.Role, m.Content)).ToList();

        var rawReply = await ai.CompleteAsync(systemPrompt, history, onBusy, ct);

        var marker = ExtractBookingMarker(rawReply);
        if (marker is not null)
        {
            var cleanReply = rawReply.Replace(marker.Value.FullMatch, "").Trim();
            // Сервер — последняя инстанция: если запись не создана, ответ ИИ «вы записаны» клиенту не уходит.
            var rejection = await TryCreateAppointmentAsync(business, conversationId, marker.Value.Json);
            return rejection ?? cleanReply;
        }

        return rawReply;
    }

    static string ScheduleText(IEnumerable<WorkSchedule> rows) =>
        string.Join("\n", rows.OrderBy(s => s.DayOfWeek).Select(s =>
            s.IsWorkingDay
                ? $"- {DayNames[s.DayOfWeek]}: {s.StartTime:HH\\:mm}–{s.EndTime:HH\\:mm}"
                : $"- {DayNames[s.DayOfWeek]}: выходной"));

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
    private async Task<string?> TryCreateAppointmentAsync(DbBusiness business, Guid conversationId, string json)
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

        var chatId = await db.Conversations.Where(c => c.Id == conversationId).Select(c => (long?)c.TelegramChatId).FirstOrDefaultAsync();

        // Время по часам бизнеса — так, как его назвал клиент (смещение часового пояса игнорируем).
        var result = await booking.BookAsync(business.Id, new BookingRequest(
            data.procedure_name, data.client_name, data.client_phone, parsed.DateTime, AppointmentSource.Bot,
            DurationMinutes: data.duration_minutes, MasterName: data.master_name,
            ConversationId: conversationId, TelegramChatId: chatId));
        if (result.Appointment is not { } appt) return result.Error;

        try
        {
            if (business.CrmWebhookUrl is { } url)
                await crmWebhook.FireAsync(url, "appointment.created", appt);

            if (business.OwnerNotificationChatId is { } ownerId && business.BotToken is { } botToken)
                await telegram.SendMessageAsync(botToken, ownerId, ownerMessages.NewBooking(appt));
        }
        catch
        {
            // Запись уже сохранена; сбой уведомления не должен ломать ответ клиенту.
        }
        return null;
    }

    private record BookingData(
        string procedure_name,
        string scheduled_at,
        int duration_minutes,
        string client_name,
        string client_phone,
        string? master_name = null);
}
