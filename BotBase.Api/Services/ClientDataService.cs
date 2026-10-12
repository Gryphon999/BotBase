using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Services;

public enum EraseResult { Done, NotFound, HasUpcoming }

// Удаление данных клиента по его просьбе (право на удаление, политика конфиденциальности, раздел «Ваши права»).
// Переписка и карточка удаляются; в записях стираются имя и телефон — сами визиты остаются в статистике и выручке.
// Будущие записи сначала нужно отменить, иначе салон молча потеряет визит.
public class ClientDataService(AppDbContext db)
{
    public const string ErasedName = "Удалён по запросу";

    public async Task<EraseResult> EraseClientAsync(Guid businessId, Guid clientId)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.BusinessId == businessId);
        return client is null ? EraseResult.NotFound : await EraseAsync(businessId, client, client.TelegramChatId);
    }

    // Из «Разговоров» (доступно на любом тарифе): если у чата есть карточка клиента — удаляем и её.
    public async Task<EraseResult> EraseConversationAsync(Guid businessId, Guid conversationId)
    {
        var chatId = await db.Conversations.Where(c => c.Id == conversationId && c.BusinessId == businessId)
            .Select(c => (long?)c.TelegramChatId).FirstOrDefaultAsync();
        if (chatId is null) return EraseResult.NotFound;
        var client = await db.Clients.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.TelegramChatId == chatId);
        return await EraseAsync(businessId, client, chatId);
    }

    async Task<EraseResult> EraseAsync(Guid businessId, Client? client, long? chatId)
    {
        var conversations = chatId is null ? [] :
            await db.Conversations.Where(c => c.BusinessId == businessId && c.TelegramChatId == chatId).ToListAsync();
        var conversationIds = conversations.Select(c => (Guid?)c.Id).ToList();
        var clientId = client?.Id;
        var appointments = await db.Appointments
            .Where(a => a.BusinessId == businessId && ((clientId != null && a.ClientId == clientId) || conversationIds.Contains(a.ConversationId)))
            .ToListAsync();

        var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
        var now = BusinessClock.Now(business.TimeZone);
        if (appointments.Any(a => a.Status != AppointmentStatus.Cancelled && a.ScheduledAt >= now))
            return EraseResult.HasUpcoming;

        foreach (var a in appointments)
        {
            a.ClientName = ErasedName;
            a.ClientPhone = "";
            a.ClientId = null;
            a.ConversationId = null;
            a.UpdatedAt = DateTime.UtcNow;
        }
        db.Conversations.RemoveRange(conversations); // сообщения удаляются каскадом
        if (client is not null) db.Clients.Remove(client);
        await db.SaveChangesAsync();
        return EraseResult.Done;
    }
}
