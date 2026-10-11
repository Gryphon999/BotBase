using Microsoft.JSInterop;
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

    // ---- Вход в кабинет: токен из localStorage, без него — на страницу входа ----
    public async Task<bool> EnsureSignedInAsync(Microsoft.JSInterop.IJSRuntime js, Microsoft.AspNetCore.Components.NavigationManager nav)
    {
        var token = await js.InvokeAsync<string?>("localStorage.getItem", "jwt");
        if (token is null) { nav.NavigateTo("/login"); return false; }
        SetToken(token);
        return true;
    }

    public static async Task<BookingProblem> ProblemAsync(HttpResponseMessage resp)
    {
        try { return await resp.Content.ReadFromJsonAsync<BookingProblem>() ?? new BookingProblem("Не получилось сохранить.", null); }
        catch { return new BookingProblem("Не получилось сохранить. Попробуйте ещё раз.", null); }
    }

    // ---- Записи (CRM) ----
    public async Task<List<AppointmentItem>> GetAppointmentsAsync(string? month = null, Guid? clientId = null)
    {
        var q = new List<string>();
        if (month is not null) q.Add($"month={month}");
        if (clientId is not null) q.Add($"clientId={clientId}");
        return await http.GetFromJsonAsync<List<AppointmentItem>>("api/appointments" + (q.Count > 0 ? "?" + string.Join("&", q) : "")) ?? [];
    }

    // Время отправляем «как на часах бизнеса», без часового пояса (см. соглашение о времени на сервере).
    static string Wall(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    public Task<HttpResponseMessage> CreateManualAppointmentAsync(string clientName, string clientPhone, Guid procedureId, DateTime at, Guid? masterId, bool ignoreWorkingHours, bool notifyClient) =>
        http.PostAsJsonAsync("api/appointments/manual", new { clientName, clientPhone, procedureId, scheduledAt = Wall(at), masterId, ignoreWorkingHours, notifyClient });

    public Task<HttpResponseMessage> RescheduleAppointmentAsync(Guid id, DateTime at, Guid? masterId, bool ignoreWorkingHours, bool notifyClient) =>
        http.PutAsJsonAsync($"api/appointments/{id}/reschedule", new { scheduledAt = Wall(at), masterId, ignoreWorkingHours, notifyClient });

    public Task<HttpResponseMessage> SetAppointmentStatusAsync(Guid id, string status, bool notifyClient) =>
        http.PatchAsJsonAsync($"api/appointments/{id}/status", new { status, notifyClient });

    public Task<HttpResponseMessage> SetNoShowAsync(Guid id, bool noShow) =>
        http.PatchAsJsonAsync($"api/appointments/{id}/no-show", new { noShow });

    public async Task<List<ProcedureItem>> GetProcedureListAsync() =>
        await http.GetFromJsonAsync<List<ProcedureItem>>("api/procedures") ?? [];

    // ---- Клиенты ----
    public async Task<List<ClientItem>> GetClientsAsync(string? search = null) =>
        await http.GetFromJsonAsync<List<ClientItem>>("api/clients" + (string.IsNullOrWhiteSpace(search) ? "" : "?search=" + Uri.EscapeDataString(search))) ?? [];
    public Task<HttpResponseMessage> CreateClientAsync(string name, string phone, string? notes) => http.PostAsJsonAsync("api/clients", new { name, phone, notes });
    public Task<HttpResponseMessage> UpdateClientAsync(Guid id, string name, string phone, string? notes) => http.PutAsJsonAsync($"api/clients/{id}", new { name, phone, notes });

    // ---- Мастера ----
    public async Task<List<MasterItem>> GetMastersAsync() => await http.GetFromJsonAsync<List<MasterItem>>("api/masters") ?? [];
    public Task<HttpResponseMessage> CreateMasterAsync(string name, List<Guid> procedureIds, List<ScheduleDay>? schedule) => http.PostAsJsonAsync("api/masters", new { name, procedureIds, schedule });
    public Task<HttpResponseMessage> UpdateMasterAsync(Guid id, string name, List<Guid> procedureIds, List<ScheduleDay>? schedule) => http.PutAsJsonAsync($"api/masters/{id}", new { name, procedureIds, schedule });
    public Task<HttpResponseMessage> DeleteMasterAsync(Guid id) => http.DeleteAsync($"api/masters/{id}");

    // ---- Автосообщения и статистика ----
    public Task<AutomationSettings?> GetAutomationAsync() => http.GetFromJsonAsync<AutomationSettings>("api/automation");
    public Task<HttpResponseMessage> SaveAutomationAsync(AutomationSettings s) => http.PutAsJsonAsync("api/automation", s);
    public Task<StatsData?> GetStatsAsync(string month) => http.GetFromJsonAsync<StatsData>($"api/stats?month={month}");

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
