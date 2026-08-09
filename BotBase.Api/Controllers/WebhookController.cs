using BotBase.Api.Data;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        var reply = await GenerateReplyAsync(business, conversation.Id, text);

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

    private async Task<string> GenerateReplyAsync(DbBusiness business, Guid conversationId, string userText)
    {
        // TODO: реализовать самостоятельно
        // 1. Собери текст всех KnowledgeChunks в одну строку через string.Join
        // 2. Сформируй systemPrompt — кто ты, какой бизнес, и база знаний
        // 3. Загрузи последние 10 сообщений разговора из db.Messages (OrderBy CreatedAt, TakeLast(10))
        // 4. Преврати их в List<(string role, string content)>
        // 5. Вызови anthropic.CompleteAsync(systemPrompt, history) и верни результат
        throw new NotImplementedException("Реализуй этот метод!");
    }
}
