using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Telegram.Bot.Types;
using DbMessage = BotBase.Api.Data.Entities.Message;
using DbConversation = BotBase.Api.Data.Entities.Conversation;
using DbBusiness = BotBase.Api.Data.Entities.Business;

namespace BotBase.Api.Controllers;

[ApiController]
public class WebhookController(
    AppDbContext db,
    TelegramService telegram,
    AnthropicService anthropic) : ControllerBase
{
    [HttpPost("webhook/{businessId:guid}")]
    public async Task<IActionResult> Handle(Guid businessId, [FromBody] Update update)
    {
        if (update.Message?.Text is not { } text || update.Message.Chat?.Id is not { } chatId)
            return Ok();

        var business = await db.Businesses
            .Include(b => b.KnowledgeChunks)
            .FirstOrDefaultAsync(b => b.Id == businessId && b.IsActive);

        if (business is null || business.BotToken is null)
            return Ok();

        var conversation = await GetOrCreateConversationAsync(businessId, chatId);

        db.Messages.Add(new DbMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "user",
            Content = text
        });
        await db.SaveChangesAsync();

        string reply;
        try
        {
            reply = await GenerateReplyAsync(business, conversation.Id, businessId, text);
        }
        catch
        {
            reply = "Извините, сервис временно недоступен. Попробуйте чуть позже.";
        }

        db.Messages.Add(new DbMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = reply
        });
        await db.SaveChangesAsync();

        await telegram.SendMessageAsync(business.BotToken, chatId, reply);
        return Ok();
    }

    private async Task<DbConversation> GetOrCreateConversationAsync(Guid businessId, long chatId)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.TelegramChatId == chatId);
        if (conv is not null) return conv;

        conv = new DbConversation
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            TelegramChatId = chatId
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        return conv;
    }

    private async Task<string> GenerateReplyAsync(
        DbBusiness business, Guid conversationId, Guid businessId, string userText)
    {
        var knowledgeText = string.Join("\n\n", business.KnowledgeChunks.Select(c => c.ExtractedText));

        // Load procedures and schedule to give AI booking context
        var procedures = await db.Procedures
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var schedule = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DayOfWeek)
            .ToListAsync();

        // Upcoming booked slots (next 7 days) to check conflicts
        var until = DateTime.UtcNow.AddDays(7);
        var bookedSlots = await db.Appointments
            .Where(a => a.BusinessId == businessId
                     && a.ScheduledAt >= DateTime.UtcNow
                     && a.ScheduledAt <= until
                     && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => $"{a.ScheduledAt:dd.MM HH:mm} — {a.ProcedureName} ({a.DurationMinutes} мин)")
            .ToListAsync();

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

        var systemPrompt = $$"""
            Ты ассистент компании "{{business.BusinessName}}".
            Отвечай только на вопросы, связанные с бизнесом. Будь вежлив и конкретен.

            База знаний:
            {{knowledgeText}}

            ДОСТУПНЫЕ ПРОЦЕДУРЫ:
            {{procedureList}}

            РАБОЧЕЕ РАСПИСАНИЕ:
            {{scheduleText}}

            ЗАНЯТЫЕ СЛОТЫ (ближайшие 7 дней):
            {{bookedText}}

            ИНСТРУКЦИЯ ПО ЗАПИСИ:
            Если клиент хочет записаться:
            1. Уточни желаемую процедуру (только из списка выше).
            2. Уточни дату и время (должны входить в рабочее расписание и не конфликтовать с занятыми слотами).
            3. Уточни имя клиента.
            4. Уточни номер телефона клиента.
            5. Когда ВСЕ 4 пункта собраны — подтверди запись клиенту и в конце ответа добавь РОВНО ОДНУ строку:
            [[BOOK:{"procedure_name":"...","scheduled_at":"2026-08-19T11:00:00","duration_minutes":60,"client_name":"...","client_phone":"..."}]]
            Используй формат даты ISO 8601. Не добавляй [[BOOK:...]] пока не собраны все данные.
            """;

        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var history = messages.Select(m => (m.Role, m.Content)).ToList();

        var rawReply = await anthropic.CompleteAsync(systemPrompt, history);

        // Parse [[BOOK:...]] marker and create appointment if found
        var booking = ExtractBookingMarker(rawReply);
        if (booking is not null)
        {
            var cleanReply = rawReply.Replace(booking.Value.FullMatch, "").Trim();
            await TryCreateAppointmentAsync(businessId, conversationId, booking.Value.Json);
            return cleanReply;
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

    private async Task TryCreateAppointmentAsync(Guid businessId, Guid conversationId, string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<BookingData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null) return;

            if (!DateTime.TryParse(data.scheduled_at, out var scheduledAt)) return;

            db.Appointments.Add(new Appointment
            {
                Id              = Guid.NewGuid(),
                BusinessId      = businessId,
                ConversationId  = conversationId,
                ProcedureName   = data.procedure_name,
                ClientName      = data.client_name,
                ClientPhone     = data.client_phone,
                ScheduledAt     = scheduledAt.Kind == DateTimeKind.Utc
                                    ? scheduledAt
                                    : scheduledAt.ToUniversalTime(),
                DurationMinutes = data.duration_minutes,
                Status          = AppointmentStatus.Pending
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            // Booking parsing failed silently — reply still sent to user
        }
    }

    private record BookingData(
        string procedure_name,
        string scheduled_at,
        int duration_minutes,
        string client_name,
        string client_phone);
}
