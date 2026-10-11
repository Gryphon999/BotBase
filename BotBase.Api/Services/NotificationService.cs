using System.Net;
using System.Net.Mail;

namespace BotBase.Api.Services;

// Уведомления техподдержки: письмо через SMTP (Gmail) и сообщение в Telegram.
// Если канал не настроен в переменных окружения, он просто пропускается — сайт продолжает работать.
public class NotificationService(IConfiguration config, PlatformSettingsService settings, TelegramService telegram, ILogger<NotificationService> log)
{
    public async Task NotifySupportAsync(string subject, string body)
    {
        var email = await settings.GetAsync(PlatformSettingsService.SupportEmail);
        if (email is not null) await SendEmailAsync(email, subject, body);

        var chatId = await settings.GetAsync(PlatformSettingsService.SupportTelegramChatId);
        var botToken = config["Support:TelegramBotToken"];
        if (chatId is not null && long.TryParse(chatId, out var id) && !string.IsNullOrEmpty(botToken))
        {
            try { await telegram.SendMessageAsync(botToken, id, $"🆘 {subject}\n\n{body}"); }
            catch (Exception ex) { log.LogWarning(ex, "Support Telegram notification failed"); }
        }
    }

    public virtual async Task SendEmailAsync(string to, string subject, string body)
    {
        var host = config["Smtp:Host"] ?? "smtp.gmail.com";
        var user = config["Smtp:User"];
        var password = config["Smtp:Password"];
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            log.LogInformation("SMTP not configured, email to {To} skipped", to);
            return;
        }
        try
        {
            using var client = new SmtpClient(host, config.GetValue("Smtp:Port", 587))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, password)
            };
            using var msg = new MailMessage(new MailAddress(config["Smtp:From"] ?? user, "BotBase"), new MailAddress(to))
            {
                Subject = subject,
                Body = body
            };
            await client.SendMailAsync(msg);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Email to {To} failed", to);
        }
    }
}
