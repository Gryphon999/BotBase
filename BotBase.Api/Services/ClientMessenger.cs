using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Services;

// Сообщения клиенту бизнеса от имени бота: напоминания, переносы, отмены, отзывы, «давно не были».
// Каждое сообщение сохраняется в разговор — если клиент ответит, ИИ увидит, на что он отвечает.
public class ClientMessenger(AppDbContext db, TelegramService telegram, ILogger<ClientMessenger> log)
{
    // Callback-данные кнопок: «c:<действие>:<id записи>» (Telegram ограничивает 64 байтами).
    public const string Confirm = "y", Cancel = "n", Rate = "r";
    public static string Data(string action, Guid appointmentId) => $"c:{action}:{appointmentId:N}";

    public async Task<long?> ChatOfAsync(Appointment a)
    {
        if (a.ClientId is { } cid && await db.Clients.Where(c => c.Id == cid).Select(c => c.TelegramChatId).FirstOrDefaultAsync() is { } chat)
            return chat;
        if (a.ConversationId is { } conv)
            return await db.Conversations.Where(c => c.Id == conv).Select(c => (long?)c.TelegramChatId).FirstOrDefaultAsync();
        return null;
    }

    public async Task<bool> SendAsync(Business business, long chatId, string text,
        IReadOnlyList<IReadOnlyList<(string Text, string Data)>>? buttons = null)
    {
        if (business.BotToken is not { } token) return false;
        try
        {
            if (buttons is null) await telegram.SendMessageAsync(token, chatId, text);
            else await telegram.SendButtonsAsync(token, chatId, text, buttons);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Message to client chat {Chat} failed", chatId);
            return false;
        }

        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.BusinessId == business.Id && c.TelegramChatId == chatId);
        if (conversation is null)
        {
            conversation = new Conversation { Id = Guid.NewGuid(), BusinessId = business.Id, TelegramChatId = chatId };
            db.Conversations.Add(conversation);
        }
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = text });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SendAsync(Business business, Appointment a, string text,
        IReadOnlyList<IReadOnlyList<(string Text, string Data)>>? buttons = null) =>
        await ChatOfAsync(a) is { } chat && await SendAsync(business, chat, text, buttons);
}

// Тексты автосообщений клиенту.
public static class ClientTexts
{
    static string When(Appointment a) => $"{a.ScheduledAt:dd.MM} в {a.ScheduledAt:HH:mm}";
    static string Who(Appointment a) => a.Master is { } m ? $", мастер {m.Name}" : "";

    public static string Reminder(Business b, Appointment a, DateTime now)
    {
        var day = a.ScheduledAt.Date == now.Date ? "сегодня" : a.ScheduledAt.Date == now.Date.AddDays(1) ? "завтра" : a.ScheduledAt.ToString("dd.MM");
        return $"Здравствуйте, {a.ClientName}! Напоминаем о записи в «{b.BusinessName}»: {day} в {a.ScheduledAt:HH:mm}, {a.ProcedureName}{Who(a)}.\n\nПодтвердите, пожалуйста, что придёте 🙏";
    }

    public static IReadOnlyList<IReadOnlyList<(string, string)>> ReminderButtons(Appointment a) =>
        [[("✅ Приду", ClientMessenger.Data(ClientMessenger.Confirm, a.Id)), ("❌ Отменить", ClientMessenger.Data(ClientMessenger.Cancel, a.Id))]];

    public const string Confirmed = "Спасибо! Ждём вас 🙂";
    public const string CancelledByClient = "Запись отменена. Если захотите выбрать другое время — просто напишите сюда.";

    public static string CancelledByOwner(Appointment a) =>
        $"Здравствуйте, {a.ClientName}! К сожалению, ваша запись на {When(a)} ({a.ProcedureName}) отменена. Чтобы выбрать другое время, просто напишите сюда 🙂";

    public static string Rescheduled(Appointment a) =>
        $"Здравствуйте, {a.ClientName}! Ваша запись перенесена: {a.ProcedureName}, {When(a)}{Who(a)}. Если это время не подходит — напишите сюда, подберём другое.";

    public static string Booked(Business b, Appointment a) =>
        $"Здравствуйте, {a.ClientName}! Вы записаны в «{b.BusinessName}»: {a.ProcedureName}, {When(a)}{Who(a)}. Если планы изменятся — напишите сюда.";

    public static string ReviewRequest(Business b) =>
        $"Спасибо, что были у нас в «{b.BusinessName}»! Как вам визит? Оцените, пожалуйста, от 1 до 5:";

    public static IReadOnlyList<IReadOnlyList<(string, string)>> RatingButtons(Appointment a) =>
        [Enumerable.Range(1, 5).Select(n => ($"{n} ⭐", ClientMessenger.Data(ClientMessenger.Rate + n, a.Id))).ToList()];

    public static string ThanksForRating(Business b, int rating) => rating >= 4
        ? "Спасибо за высокую оценку! 💜" + (string.IsNullOrWhiteSpace(b.ReviewUrl) ? "" : $"\nБудем очень благодарны за отзыв: {b.ReviewUrl}")
        : "Спасибо за честную оценку. Мы передали ваш отзыв администратору — нам важно стать лучше.";

    public static string Winback(Business b, Client c) =>
        $"Здравствуйте, {c.Name}! Давно не виделись в «{b.BusinessName}» 🙂 Хотите записаться? Напишите удобный день — подберём время.";
}
