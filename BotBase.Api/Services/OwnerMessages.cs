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
}
