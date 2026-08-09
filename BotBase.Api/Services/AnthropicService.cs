using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BotBase.Api.Services;

public class AnthropicService(IHttpClientFactory httpFactory, IConfiguration config)
{
    public async Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history)
    {
        var apiKey = config["Anthropic:ApiKey"];
        var client = httpFactory.CreateClient();

        var request = new GeminiRequest(
            SystemInstruction: new GeminiSystem([new GeminiPart(systemPrompt)]),
            Contents: history.Select(h => new GeminiContent(
                Role: h.role == "assistant" ? "model" : "user",
                Parts: [new GeminiPart(h.content)])).ToList());

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();

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
