using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using DbMessage = BotBase.Api.Data.Entities.Message;
using DbConversation = BotBase.Api.Data.Entities.Conversation;

namespace BotBase.Api.Controllers;

// Webhook только сохраняет сообщение клиента и ставит чат в очередь — сразу отвечаем Telegram «принято».
// Ответ формирует BotWorker: склеивает серию сообщений, ждёт свободный лимит ИИ, показывает «печатает…».
[ApiController]
public class WebhookController(AppDbContext db, TelegramService telegram, ChatQueue queue, OwnerMessages ownerMessages) : ControllerBase
{
    [HttpPost("webhook/{businessId:guid}")]
    public async Task<IActionResult> Handle(Guid businessId, [FromBody] Update update)
    {
        if (update.Message?.Text is not { } text || update.Message.Chat?.Id is not { } chatId)
            return Ok();

        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId && b.IsActive);
        if (business is null || business.BotToken is null)
            return Ok();

        var isOwner = business.OwnerNotificationChatId == chatId;
        if (text == "/start" && (business.OwnerNotificationChatId is null || isOwner))
        {
            business.OwnerNotificationChatId = chatId;
            await db.SaveChangesAsync();
            await telegram.SendMessageAsync(business.BotToken, chatId, ownerMessages.Welcome);
            return Ok();
        }

        // Ссылку на кабинет получает только владелец; у клиентов команда уходит боту как обычный текст.
        if (isOwner && OwnerMessages.IsCabinetCommand(text))
        {
            await telegram.SendMessageAsync(business.BotToken, chatId, ownerMessages.Cabinet);
            return Ok();
        }

        var conversation = await GetOrCreateConversationAsync(businessId, chatId);
        db.Messages.Add(new DbMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "user",
            Content = text
        });
        await db.SaveChangesAsync();

        queue.Enqueue(businessId, chatId);
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
}
