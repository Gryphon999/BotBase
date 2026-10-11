using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
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
public class WebhookController(AppDbContext db, TelegramService telegram, ChatQueue queue, OwnerMessages ownerMessages,
    ClientMessenger messenger, CrmWebhookService crmWebhook) : ControllerBase
{
    [HttpPost("webhook/{businessId:guid}")]
    public async Task<IActionResult> Handle(Guid businessId, [FromBody] Update update)
    {
        if (update.CallbackQuery is { } callback)
            return await HandleButtonAsync(businessId, callback);

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

    // Кнопки под напоминанием («Приду» / «Отменить») и под просьбой об отзыве (1–5 ⭐).
    private async Task<IActionResult> HandleButtonAsync(Guid businessId, CallbackQuery callback)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId && b.IsActive);
        if (business?.BotToken is not { } token || callback.Message?.Chat is not { } chat) return Ok();

        var parts = (callback.Data ?? "").Split(':');
        if (parts is not ["c", var action, var rawId] || !Guid.TryParseExact(rawId, "N", out var appointmentId))
        {
            await telegram.AnswerCallbackAsync(token, callback.Id);
            return Ok();
        }

        var appt = await db.Appointments.Include(a => a.Master)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.BusinessId == businessId);
        // Нажать кнопку может только тот клиент, которому она пришла.
        if (appt is null || await messenger.ChatOfAsync(appt) != chat.Id)
        {
            await telegram.AnswerCallbackAsync(token, callback.Id, "Запись не найдена");
            return Ok();
        }
        await telegram.RemoveButtonsAsync(token, chat.Id, callback.Message.Id);
        var now = BusinessClock.Now(business.TimeZone);

        if (action == ClientMessenger.Confirm)
        {
            if (appt.Status == AppointmentStatus.Cancelled)
            {
                await telegram.AnswerCallbackAsync(token, callback.Id, "Эта запись уже отменена");
                return Ok();
            }
            appt.ClientConfirmedAt ??= DateTime.UtcNow;
            appt.Status = AppointmentStatus.Confirmed;
            appt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await telegram.AnswerCallbackAsync(token, callback.Id, "Спасибо!");
            await messenger.SendAsync(business, chat.Id, ClientTexts.Confirmed);
            if (business.OwnerNotificationChatId is { } owner) await telegram.SendMessageAsync(token, owner, ownerMessages.ClientConfirmed(appt));
            if (business.CrmWebhookUrl is { } url) await crmWebhook.FireAsync(url, "appointment.confirmed", appt);
        }
        else if (action == ClientMessenger.Cancel)
        {
            if (appt.Status == AppointmentStatus.Cancelled || appt.ScheduledAt < now)
            {
                await telegram.AnswerCallbackAsync(token, callback.Id, "Эту запись уже нельзя отменить");
                return Ok();
            }
            appt.Status = AppointmentStatus.Cancelled;
            appt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await telegram.AnswerCallbackAsync(token, callback.Id, "Запись отменена");
            await messenger.SendAsync(business, chat.Id, ClientTexts.CancelledByClient);
            if (business.OwnerNotificationChatId is { } owner) await telegram.SendMessageAsync(token, owner, ownerMessages.ClientCancelled(appt));
            if (business.CrmWebhookUrl is { } url) await crmWebhook.FireAsync(url, "appointment.cancelled", appt);
        }
        else if (action.Length == 2 && action[0] == ClientMessenger.Rate[0] && action[1] is >= '1' and <= '5')
        {
            var rating = action[1] - '0';
            var first = appt.Rating is null;
            appt.Rating = rating;
            await db.SaveChangesAsync();
            await telegram.AnswerCallbackAsync(token, callback.Id, "Спасибо за оценку!");
            if (first)
            {
                await messenger.SendAsync(business, chat.Id, ClientTexts.ThanksForRating(business, rating));
                if (rating <= 3 && business.OwnerNotificationChatId is { } owner)
                    await telegram.SendMessageAsync(token, owner, ownerMessages.LowRating(appt, rating));
            }
        }
        else
        {
            await telegram.AnswerCallbackAsync(token, callback.Id);
        }
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
