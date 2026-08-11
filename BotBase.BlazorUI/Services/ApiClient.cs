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

    public Task<HttpResponseMessage> GetKnowledgeAsync() =>
        http.GetAsync("api/knowledge");

    public async Task<HttpResponseMessage> UploadFileAsync(IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(maxAllowedSize: 10_000_000);
        content.Add(new StreamContent(stream), "file", file.Name);
        return await http.PostAsync("api/knowledge", content);
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
}
