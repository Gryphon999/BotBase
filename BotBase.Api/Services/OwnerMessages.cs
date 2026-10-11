using BotBase.Api.Data.Entities;

namespace BotBase.Api.Services;

// Сообщения владельцу бизнеса в Telegram. В каждом есть ссылка на кабинет:
// владелец живёт в Telegram и не должен терять адрес сайта (просьба 11.10.2026).
public class OwnerMessages(IConfiguration config)
{
    public string CabinetUrl => $"{config["WebhookBaseUrl"]?.TrimEnd('/')}/dashboard";

    // Команды для владельца: /cabinet (латиницей — так Telegram делает её кликабельной) и /кабинет.
    public static bool IsCabinetCommand(string text)
    {
        var command = text.Trim().Split(' ', '@')[0].ToLowerInvariant();
        return command is "/cabinet" or "/кабинет";
    }

    public string Welcome =>
        "✅ Вы подключены как владелец! Теперь вы будете получать уведомления о новых записях клиентов.\n\n" +
        $"🔗 Ваш кабинет BotBase: {CabinetUrl}\n" +
        "Потеряете ссылку — напишите боту /cabinet.";

    public string Cabinet =>
        $"🔗 Ваш кабинет BotBase: {CabinetUrl}\n\n" +
        "Там записи, разговоры с клиентами, услуги, расписание и база знаний.";

    public string CabinetFooter => $"\n\n🔗 Кабинет: {CabinetUrl}";

    public string NewBooking(Appointment a) =>
        "📅 Новая запись!\n" +
        $"Клиент: {a.ClientName}\n" +
        $"Телефон: {a.ClientPhone}\n" +
        $"Процедура: {a.ProcedureName}\n" +
        (a.Master is { } m ? $"Мастер: {m.Name}\n" : "") +
        $"Дата и время: {a.ScheduledAt:dd.MM.yyyy HH:mm}\n" +
        $"Длительность: {a.DurationMinutes} мин" +
        CabinetFooter;

    public string ClientConfirmed(Appointment a) =>
        $"✅ {a.ClientName} подтвердил(а) запись на {a.ScheduledAt:dd.MM HH:mm} ({a.ProcedureName}).";

    public string ClientCancelled(Appointment a) =>
        $"❌ {a.ClientName} отменил(а) запись на {a.ScheduledAt:dd.MM HH:mm} ({a.ProcedureName}). Время освободилось." +
        CabinetFooter;

    public string LowRating(Appointment a, int rating) =>
        $"⚠️ {a.ClientName} оценил(а) визит {a.ScheduledAt:dd.MM} ({a.ProcedureName}) на {rating} из 5. " +
        $"Стоит связаться: {a.ClientPhone}" + CabinetFooter;
}
