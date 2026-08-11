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

        var request = new GeminiRequest(
            SystemInstruction: new GeminiSystem([new GeminiPart(systemPrompt)]),
            Contents: history.Select(h => new GeminiContent(
                Role: h.role == "assistant" ? "model" : "user",
                Parts: [new GeminiPart(h.content)])).ToList());

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
        var response = await client.PostAsJsonAsync(url, request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError("Gemini error {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
               ?? "Не удалось получить ответ";
    }

    private record GeminiRequest(
        [property: JsonPropertyName("system_instruction")] GeminiSystem SystemInstruction,
        List<GeminiContent> Contents);

    private record GeminiSystem(List<GeminiPart> Parts);

    private record GeminiContent(string Role, List<GeminiPart> Parts);

    private record GeminiPart(string Text);

    private record GeminiResponse(List<GeminiCandidate>? Candidates);

    private record GeminiCandidate(GeminiContent? Content);
}
