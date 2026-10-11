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

    public virtual async Task SendMessageAsync(string token, long chatId, string text)
    {
        var client = httpFactory.CreateClient();
        await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new { chat_id = chatId, text });
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
