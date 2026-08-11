using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BotBase.Api.Services;

public class AnthropicService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AnthropicService> logger, GeminiRateLimiter rateLimiter)
{
    public async Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history)
    {
        if (!rateLimiter.TryAcquire())
            throw new InvalidOperationException("Превышен лимит запросов к Gemini. Попробуйте через минуту.");

        var apiKey = config["Anthropic:ApiKey"];
        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

        var messages = new List<ChatMessage> { new("system", systemPrompt) };
        messages.AddRange(history.Select(h => new ChatMessage(h.role, h.content)));

        var request = new ChatRequest("gemini-2.5-flash", messages);
        var url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

        var response = await client.PostAsJsonAsync(url, request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError("Gemini error {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "Не удалось получить ответ";
    }

    private record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages);

    private record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    private record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
