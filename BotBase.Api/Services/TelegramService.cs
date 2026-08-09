using System.Net.Http.Json;

namespace BotBase.Api.Services;

public class TelegramService(IHttpClientFactory httpFactory)
{
    public async Task<string?> GetBotUsernameAsync(string token)
    {
        var client = httpFactory.CreateClient();
        var resp = await client.GetFromJsonAsync<TelegramMeResponse>(
            $"https://api.telegram.org/bot{token}/getMe");
        return resp?.Ok == true ? resp.Result?.Username : null;
    }

    public async Task<bool> SetWebhookAsync(string token, string webhookUrl)
    {
        var client = httpFactory.CreateClient();
        var resp = await client.GetAsync(
            $"https://api.telegram.org/bot{token}/setWebhook?url={Uri.EscapeDataString(webhookUrl)}");
        return resp.IsSuccessStatusCode;
    }

    public async Task SendMessageAsync(string token, long chatId, string text)
    {
        var client = httpFactory.CreateClient();
        await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new { chat_id = chatId, text });
    }

    private record TelegramMeResponse(bool Ok, TelegramUser? Result);
    private record TelegramUser(string Username);
}
