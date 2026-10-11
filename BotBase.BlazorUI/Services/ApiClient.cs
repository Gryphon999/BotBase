using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BotBase.BlazorUI.Services;

public class ApiClient(HttpClient http)
{
    public void SetToken(string token) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public void ClearToken() =>
        http.DefaultRequestHeaders.Authorization = null;

    public Task<HttpResponseMessage> RegisterAsync(string email, string password, string businessName) =>
        http.PostAsJsonAsync("api/auth/register", new { email, password, businessName });

    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        http.PostAsJsonAsync("api/auth/login", new { email, password });

    public Task<HttpResponseMessage> SetupBotAsync(string botToken) =>
        http.PostAsJsonAsync("api/bots", new { botToken });

    public Task<HttpResponseMessage> GetBotStatusAsync() =>
        http.GetAsync("api/bots");

    public Task<HttpResponseMessage> ResetOwnerNotificationAsync() =>
        http.DeleteAsync("api/bots/owner-notification");

    public Task<HttpResponseMessage> GetKnowledgeAsync() =>
        http.GetAsync("api/knowledge");

    public async Task<HttpResponseMessage> UploadFileAsync(IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(maxAllowedSize: 10_000_000);
        content.Add(new StreamContent(stream), "file", file.Name);
        return await http.PostAsync("api/knowledge", content);
    }

    public record KnowledgeItem(Guid Id, string FileName, DateTime UploadedAt, string Text, bool Broken);

    public async Task<KnowledgeItem?> GetKnowledgeItemAsync(Guid id)
    {
        var resp = await http.GetAsync($"api/knowledge/{id}");
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<KnowledgeItem>() : null;
    }

    public Task<HttpResponseMessage> DeleteKnowledgeAsync(Guid id) =>
        http.DeleteAsync($"api/knowledge/{id}");

    public Task<HttpResponseMessage> GetConversationsAsync() =>
        http.GetAsync("api/conversations");

    public Task<HttpResponseMessage> GetMessagesAsync(Guid conversationId) =>
        http.GetAsync($"api/conversations/{conversationId}/messages");

    public Task<HttpResponseMessage> GetProceduresAsync() =>
        http.GetAsync("api/procedures");

    public Task<HttpResponseMessage> CreateProcedureAsync(string name, int durationMinutes, decimal price) =>
        http.PostAsJsonAsync("api/procedures", new { name, durationMinutes, price });

    public Task<HttpResponseMessage> UpdateProcedureAsync(Guid id, string name, int durationMinutes, decimal price) =>
        http.PutAsJsonAsync($"api/procedures/{id}", new { name, durationMinutes, price });

    public Task<HttpResponseMessage> DeleteProcedureAsync(Guid id) =>
        http.DeleteAsync($"api/procedures/{id}");

    public Task<HttpResponseMessage> GetScheduleAsync() =>
        http.GetAsync("api/schedule");

    public Task<HttpResponseMessage> SaveScheduleAsync(object rows) =>
        http.PutAsJsonAsync("api/schedule", rows);

    public Task<HttpResponseMessage> GetApiKeyAsync() =>
        http.GetAsync("api/integration/apikey");

    public Task<HttpResponseMessage> GetWebhookAsync() =>
        http.GetAsync("api/integration/webhook");

    public Task<HttpResponseMessage> SetWebhookAsync(string? url) =>
        http.PutAsJsonAsync("api/integration/webhook", new { url });

    // ---- Пароли ----
    public Task<HttpResponseMessage> ForgotPasswordAsync(string email) => http.PostAsJsonAsync("api/auth/forgot-password", new { email });
    public Task<HttpResponseMessage> ResetPasswordAsync(string token, string newPassword) => http.PostAsJsonAsync("api/auth/reset-password", new { token, newPassword });
    public Task<HttpResponseMessage> ChangePasswordAsync(string currentPassword, string newPassword) => http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword });
    public Task<HttpResponseMessage> AdminResetPasswordAsync(Guid id) => http.PostAsync($"api/admin/businesses/{id}/reset-password", null);

    // ---- Главная ----
    public Task<HttpResponseMessage> GetDashboardAsync() => http.GetAsync("api/dashboard");

    // ---- Поддержка (клиент) ----
    public Task<HttpResponseMessage> GetTicketsAsync() => http.GetAsync("api/support/tickets");
    public Task<HttpResponseMessage> GetTicketAsync(Guid id) => http.GetAsync($"api/support/tickets/{id}");
    public Task<HttpResponseMessage> CreateTicketAsync(string subject, string text) => http.PostAsJsonAsync("api/support/tickets", new { subject, text });
    public Task<HttpResponseMessage> ReplyTicketAsync(Guid id, string text) => http.PostAsJsonAsync($"api/support/tickets/{id}/messages", new { text });

    // ---- Админка ----
    public Task<HttpResponseMessage> AdminLoginAsync(string email, string password) => http.PostAsJsonAsync("api/admin/auth/login", new { email, password });
    public Task<HttpResponseMessage> AdminStatsAsync() => http.GetAsync("api/admin/stats");
    public Task<HttpResponseMessage> AdminBusinessesAsync() => http.GetAsync("api/admin/businesses");
    public Task<HttpResponseMessage> AdminBotInfoAsync(Guid id) => http.GetAsync($"api/admin/businesses/{id}/bot");
    public Task<HttpResponseMessage> AdminSetActiveAsync(Guid id, bool active) => http.PostAsync($"api/admin/businesses/{id}/{(active ? "enable" : "disable")}", null);
    public Task<HttpResponseMessage> AdminRestartBotAsync(Guid id) => http.PostAsync($"api/admin/businesses/{id}/restart-bot", null);
    public Task<HttpResponseMessage> AdminImpersonateAsync(Guid id) => http.PostAsync($"api/admin/businesses/{id}/impersonate", null);
    public Task<HttpResponseMessage> AdminDeleteAsync(Guid id, string confirmName) => http.DeleteAsync($"api/admin/businesses/{id}?confirmName={Uri.EscapeDataString(confirmName)}");
    public Task<HttpResponseMessage> AdminGetSettingsAsync() => http.GetAsync("api/admin/settings");
    public Task<HttpResponseMessage> AdminSaveSettingsAsync(string? supportEmail, string? supportTelegramChatId) => http.PutAsJsonAsync("api/admin/settings", new { supportEmail, supportTelegramChatId });
    public Task<HttpResponseMessage> AdminTicketsAsync(string? status) => http.GetAsync("api/admin/support/tickets" + (status is null ? "" : "?status=" + status));
    public Task<HttpResponseMessage> AdminTicketAsync(Guid id) => http.GetAsync($"api/admin/support/tickets/{id}");
    public Task<HttpResponseMessage> AdminReplyTicketAsync(Guid id, string text) => http.PostAsJsonAsync($"api/admin/support/tickets/{id}/reply", new { text });
    public Task<HttpResponseMessage> AdminCloseTicketAsync(Guid id) => http.PostAsync($"api/admin/support/tickets/{id}/close", null);
}
