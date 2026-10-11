using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace BotBase.Api.Services;

// Восстановление и смена паролей клиентов.
public class PasswordService(AppDbContext db, NotificationService notify, TelegramService telegram, IConfiguration config, ILogger<PasswordService> log)
{
    public const int MinLength = 6;
    static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    // Письмо со ссылкой. Ответ всегда одинаковый, чтобы по нему нельзя было проверить, зарегистрирован ли email.
    public async Task RequestResetAsync(string email)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == email.Trim().ToLower());
        if (business is null || !business.IsActive) return;

        // Не чаще раза в минуту на один аккаунт — защита от рассылки писем по кругу.
        if (business.PasswordResetExpiresAt is { } exp && exp - TokenLifetime > DateTime.UtcNow.AddMinutes(-1)) return;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        business.PasswordResetTokenHash = Hash(token);
        business.PasswordResetExpiresAt = DateTime.UtcNow.Add(TokenLifetime);
        await db.SaveChangesAsync();

        var link = $"{config["WebhookBaseUrl"]?.TrimEnd('/')}/reset-password?token={token}";
        await notify.SendEmailAsync(business.Email, "BotBase: восстановление пароля",
            $"Здравствуйте!\n\nЧтобы задать новый пароль для «{business.BusinessName}», перейдите по ссылке (действует 1 час):\n{link}\n\n" +
            "Если вы не запрашивали восстановление, просто проигнорируйте это письмо.");

        // Второй канал, который работает и без почты: чат владельца с его ботом (туда же приходят уведомления о записях).
        // Ссылку получит только тот, кто подключён владельцем через /start.
        if (business.BotToken is { } botToken && business.OwnerNotificationChatId is { } ownerChat)
        {
            try
            {
                await telegram.SendMessageAsync(botToken, ownerChat,
                    $"🔐 Восстановление пароля BotBase\n\nСсылка для нового пароля (действует 1 час):\n{link}\n\n" +
                    "Если вы не запрашивали восстановление, просто проигнорируйте это сообщение.");
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Password reset Telegram message failed for business {Id}", business.Id);
            }
        }
        log.LogInformation("Password reset requested for business {Id}", business.Id);
    }

    public async Task<bool> ResetAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || newPassword.Length < MinLength) return false;
        var hash = Hash(token);
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.PasswordResetTokenHash == hash);
        if (business is null || business.PasswordResetExpiresAt < DateTime.UtcNow) return false;

        SetPassword(business, newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ChangeAsync(Guid businessId, string currentPassword, string newPassword)
    {
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null || newPassword.Length < MinLength || !BCrypt.Net.BCrypt.Verify(currentPassword, business.PasswordHash))
            return false;
        SetPassword(business, newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    // Временный пароль от администратора: читаемый, без похожих символов (0/O, 1/l).
    public async Task<string?> AdminResetAsync(Guid businessId)
    {
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return null;
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var temp = string.Concat(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
        SetPassword(business, temp);
        await db.SaveChangesAsync();
        return temp;
    }

    static void SetPassword(Business business, string password)
    {
        business.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        business.PasswordResetTokenHash = null;
        business.PasswordResetExpiresAt = null;
    }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
