using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Services;

using Buttons = IReadOnlyList<IReadOnlyList<(string Text, string Data)>>;

// Сообщения клиенту бизнеса от имени бота: напоминания, переносы, отмены, отзывы, «давно не были».
// Язык — тот, на котором клиент пишет боту (армянский / английский / русский); если он ещё не писал —
// язык по умолчанию из настроек бизнеса. Каждое сообщение сохраняется в разговор — ИИ увидит контекст ответа.
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

    public async Task<string> LanguageAsync(Business business, long chatId)
    {
        var recent = await db.Messages
            .Where(m => m.Role == "user" && m.Conversation.BusinessId == business.Id && m.Conversation.TelegramChatId == chatId)
            .OrderByDescending(m => m.CreatedAt).Take(5).Select(m => m.Content).ToListAsync();
        return ClientTexts.Detect(string.Join(" ", recent)) ?? ClientTexts.Normalize(business.MessagesLanguage);
    }

    public async Task<bool> SendAsync(Business business, long chatId, Func<string, string> text, Func<string, Buttons>? buttons = null)
    {
        if (business.BotToken is not { } token) return false;
        var lang = await LanguageAsync(business, chatId);
        var body = text(lang);
        try
        {
            if (buttons is null) await telegram.SendMessageAsync(token, chatId, body);
            else await telegram.SendButtonsAsync(token, chatId, body, buttons(lang));
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
        db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = body });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SendAsync(Business business, Appointment a, Func<string, string> text, Func<string, Buttons>? buttons = null) =>
        await ChatOfAsync(a) is { } chat && await SendAsync(business, chat, text, buttons);
}

// Тексты автосообщений клиенту на трёх языках: ru — русский, hy — армянский, en — английский.
public static class ClientTexts
{
    public static readonly string[] Languages = ["ru", "hy", "en"];
    public static string Normalize(string? lang) => lang is "hy" or "en" ? lang : "ru";

    // Язык по буквам в сообщениях клиента: армянские, кириллица или латиница — чего больше.
    public static string? Detect(string text)
    {
        int hy = 0, ru = 0, en = 0;
        foreach (var ch in text)
        {
            if (ch is >= 'Ա' and <= '֏') hy++;
            else if (ch is >= 'Ѐ' and <= 'ӿ') ru++;
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') en++;
        }
        if (hy + ru + en < 3) return null;
        return hy >= ru && hy >= en ? "hy" : ru >= en ? "ru" : "en";
    }

    static string T(string lang, string ru, string hy, string en) => lang switch { "hy" => hy, "en" => en, _ => ru };

    static string Time(Appointment a) => a.ScheduledAt.ToString("HH:mm");
    static string Date(Appointment a) => a.ScheduledAt.ToString("dd.MM");
    static string When(string l, Appointment a) => T(l, $"{Date(a)} в {Time(a)}", $"{Date(a)}-ին ժամը {Time(a)}-ին", $"on {Date(a)} at {Time(a)}");
    static string Who(string l, Appointment a) => a.Master is { } m ? T(l, $", мастер {m.Name}", $", վարպետ՝ {m.Name}", $", with {m.Name}") : "";

    public static string Reminder(string l, Business b, Appointment a, DateTime now)
    {
        var day = a.ScheduledAt.Date == now.Date ? T(l, "сегодня", "այսօր", "today")
                : a.ScheduledAt.Date == now.Date.AddDays(1) ? T(l, "завтра", "վաղը", "tomorrow")
                : Date(a);
        return T(l,
            $"Здравствуйте, {a.ClientName}! Напоминаем о записи в «{b.BusinessName}»: {day} в {Time(a)}, {a.ProcedureName}{Who(l, a)}.\n\nПодтвердите, пожалуйста, что придёте 🙏",
            $"Բարև ձեզ, {a.ClientName}։ Հիշեցնում ենք ձեր գրանցման մասին «{b.BusinessName}»-ում՝ {day} ժամը {Time(a)}-ին, {a.ProcedureName}{Who(l, a)}։\n\nԽնդրում ենք հաստատել, որ կգաք 🙏",
            $"Hello, {a.ClientName}! A reminder about your appointment at “{b.BusinessName}”: {day} at {Time(a)}, {a.ProcedureName}{Who(l, a)}.\n\nPlease confirm that you will come 🙏");
    }

    public static Buttons ReminderButtons(string l, Appointment a) =>
        [[(T(l, "✅ Приду", "✅ Կգամ", "✅ I'll come"), ClientMessenger.Data(ClientMessenger.Confirm, a.Id)),
          (T(l, "❌ Отменить", "❌ Չեղարկել", "❌ Cancel"), ClientMessenger.Data(ClientMessenger.Cancel, a.Id))]];

    public static string Confirmed(string l) => T(l, "Спасибо! Ждём вас 🙂", "Շնորհակալություն։ Սպասում ենք ձեզ 🙂", "Thank you! See you soon 🙂");

    public static string CancelledByClient(string l) => T(l,
        "Запись отменена. Если захотите выбрать другое время — просто напишите сюда.",
        "Գրանցումը չեղարկված է։ Եթե ցանկանաք ընտրել այլ ժամ, պարզապես գրեք այստեղ։",
        "Your appointment is cancelled. If you'd like another time, just write here.");

    public static string CancelledByOwner(string l, Appointment a) => T(l,
        $"Здравствуйте, {a.ClientName}! К сожалению, ваша запись на {When(l, a)} ({a.ProcedureName}) отменена. Чтобы выбрать другое время, просто напишите сюда 🙂",
        $"Բարև ձեզ, {a.ClientName}։ Ցավոք, ձեր գրանցումը {When(l, a)} ({a.ProcedureName}) չեղարկվել է։ Այլ ժամ ընտրելու համար պարզապես գրեք այստեղ 🙂",
        $"Hello, {a.ClientName}! Unfortunately, your appointment {When(l, a)} ({a.ProcedureName}) has been cancelled. To choose another time, just write here 🙂");

    public static string Rescheduled(string l, Appointment a) => T(l,
        $"Здравствуйте, {a.ClientName}! Ваша запись перенесена: {a.ProcedureName}, {When(l, a)}{Who(l, a)}. Если это время не подходит — напишите сюда, подберём другое.",
        $"Բարև ձեզ, {a.ClientName}։ Ձեր գրանցումը տեղափոխվել է՝ {a.ProcedureName}, {When(l, a)}{Who(l, a)}։ Եթե այս ժամը չի համապատասխանում, գրեք այստեղ, և մենք կընտրենք այլ ժամ։",
        $"Hello, {a.ClientName}! Your appointment has been moved: {a.ProcedureName}, {When(l, a)}{Who(l, a)}. If this time doesn't suit you, write here and we'll find another.");

    public static string Booked(string l, Business b, Appointment a) => T(l,
        $"Здравствуйте, {a.ClientName}! Вы записаны в «{b.BusinessName}»: {a.ProcedureName}, {When(l, a)}{Who(l, a)}. Если планы изменятся — напишите сюда.",
        $"Բարև ձեզ, {a.ClientName}։ Դուք գրանցված եք «{b.BusinessName}»-ում՝ {a.ProcedureName}, {When(l, a)}{Who(l, a)}։ Եթե ծրագրերը փոխվեն, գրեք այստեղ։",
        $"Hello, {a.ClientName}! You are booked at “{b.BusinessName}”: {a.ProcedureName}, {When(l, a)}{Who(l, a)}. If your plans change, write here.");

    public static string ReviewRequest(string l, Business b) => T(l,
        $"Спасибо, что были у нас в «{b.BusinessName}»! Как вам визит? Оцените, пожалуйста, от 1 до 5:",
        $"Շնորհակալություն, որ այցելեցիք «{b.BusinessName}»։ Ինչպե՞ս էր այցը։ Խնդրում ենք գնահատել 1-ից 5 բալով։",
        $"Thank you for visiting “{b.BusinessName}”! How was your visit? Please rate it from 1 to 5:");

    public static Buttons RatingButtons(Appointment a) =>
        [Enumerable.Range(1, 5).Select(n => ($"{n} ⭐", ClientMessenger.Data(ClientMessenger.Rate + n, a.Id))).ToList()];

    public static string ThanksForRating(string l, Business b, int rating) => rating >= 4
        ? T(l, "Спасибо за высокую оценку! 💜", "Շնորհակալություն բարձր գնահատականի համար 💜", "Thank you for the great rating! 💜")
          + (string.IsNullOrWhiteSpace(b.ReviewUrl) ? "" : "\n" + T(l, "Будем очень благодарны за отзыв: ", "Շատ շնորհակալ կլինենք ձեր կարծիքի համար՝ ", "We'd be very grateful for a review: ") + b.ReviewUrl)
        : T(l, "Спасибо за честную оценку. Мы передали ваш отзыв администратору — нам важно стать лучше.",
               "Շնորհակալություն անկեղծ գնահատականի համար։ Մենք ձեր կարծիքը փոխանցել ենք ադմինիստրատորին․ մեզ համար կարևոր է դառնալ ավելի լավը։",
               "Thank you for your honest rating. We've passed your feedback to the manager — it matters to us to get better.");

    public static string Winback(string l, Business b, Client c) => T(l,
        $"Здравствуйте, {c.Name}! Давно не виделись в «{b.BusinessName}» 🙂 Хотите записаться? Напишите удобный день — подберём время.",
        $"Բարև ձեզ, {c.Name}։ Վաղուց չենք տեսնվել «{b.BusinessName}»-ում 🙂 Կցանկանա՞ք գրանցվել։ Գրեք ձեզ հարմար օրը, և մենք կընտրենք ժամը։",
        $"Hello, {c.Name}! We haven't seen you at “{b.BusinessName}” for a while 🙂 Would you like to book? Tell us a convenient day and we'll find a time.");

    // Короткие подсказки на нажатую кнопку (всплывают над чатом).
    public static string ButtonThanks(string l) => T(l, "Спасибо!", "Շնորհակալություն։", "Thank you!");
    public static string ButtonCancelled(string l) => T(l, "Запись отменена", "Գրանցումը չեղարկված է", "Cancelled");
    public static string ButtonTooLate(string l) => T(l, "Эту запись уже нельзя изменить", "Այս գրանցումն այլևս հնարավոր չէ փոխել", "This appointment can no longer be changed");
    public static string ButtonNotFound(string l) => T(l, "Запись не найдена", "Գրանցումը չի գտնվել", "Appointment not found");
}
