using System.Net.Http.Json;

namespace BotBase.Api.Services;

public class TelegramService(IHttpClientFactory httpFactory)
{
    public virtual async Task<string?> GetBotUsernameAsync(string token)
    {
        try
        {
            var client = httpFactory.CreateClient();
            using var response = await client.GetAsync($"https://api.telegram.org/bot{token}/getMe");
            if (!response.IsSuccessStatusCode) return null;
            var resp = await response.Content.ReadFromJsonAsync<TelegramMeResponse>();
            return resp?.Ok == true ? resp.Result?.Username : null;
        }
        catch
        {
            return null;
        }
    }

    public virtual async Task<bool> SetWebhookAsync(string token, string webhookUrl)
    {
        var client = httpFactory.CreateClient();
        var resp = await client.GetAsync(
            $"https://api.telegram.org/bot{token}/setWebhook?url={Uri.EscapeDataString(webhookUrl)}");
        return resp.IsSuccessStatusCode;
    }

    // «печатает…» в чате клиента, пока бот готовит ответ (держится ~5 секунд).
    public virtual async Task SendTypingAsync(string token, long chatId)
    {
        try
        {
            var client = httpFactory.CreateClient();
            await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendChatAction", new { chat_id = chatId, action = "typing" });
        }
        catch { /* индикатор не критичен */ }
    }

    public virtual async Task SendMessageAsync(string token, long chatId, string text)
    {
        var client = httpFactory.CreateClient();
        await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new { chat_id = chatId, text });
    }

    // Сообщение с кнопками под ним (по строке на ряд). Data возвращается в webhook при нажатии (до 64 байт).
    public virtual async Task SendButtonsAsync(string token, long chatId, string text, IReadOnlyList<IReadOnlyList<(string Text, string Data)>> rows)
    {
        var client = httpFactory.CreateClient();
        await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new
            {
                chat_id = chatId,
                text,
                reply_markup = new { inline_keyboard = rows.Select(r => r.Select(b => new { text = b.Text, callback_data = b.Data })) }
            });
    }

    // Убирает «часики» на нажатой кнопке и показывает короткую подсказку.
    public virtual async Task AnswerCallbackAsync(string token, string callbackId, string? text = null)
    {
        try
        {
            var client = httpFactory.CreateClient();
            await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/answerCallbackQuery", new { callback_query_id = callbackId, text });
        }
        catch { /* не критично */ }
    }

    // Убирает кнопки у сообщения, чтобы на них нельзя было нажать второй раз.
    public virtual async Task RemoveButtonsAsync(string token, long chatId, int messageId)
    {
        try
        {
            var client = httpFactory.CreateClient();
            await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/editMessageReplyMarkup",
                new { chat_id = chatId, message_id = messageId, reply_markup = new { inline_keyboard = Array.Empty<object>() } });
        }
        catch { /* не критично */ }
    }

    public async Task<WebhookInfo?> GetWebhookInfoAsync(string token)
    {
        try
        {
            var client = httpFactory.CreateClient();
            var resp = await client.GetFromJsonAsync<WebhookInfoResponse>($"https://api.telegram.org/bot{token}/getWebhookInfo");
            return resp?.Result;
        }
        catch
        {
            return null;
        }
    }

    public record WebhookInfo(
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("pending_update_count")] int PendingUpdateCount,
        [property: System.Text.Json.Serialization.JsonPropertyName("last_error_message")] string? LastErrorMessage);
    private record WebhookInfoResponse(bool Ok, WebhookInfo? Result);

    private record TelegramMeResponse(bool Ok, TelegramUser? Result);
    private record TelegramUser(string Username);
}
