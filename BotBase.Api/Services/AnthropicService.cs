using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BotBase.Api.Services;

public class AnthropicService(IHttpClientFactory httpFactory, IConfiguration config)
{
    public async Task<string> CompleteAsync(string systemPrompt, List<(string role, string content)> history)
    {
        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", config["Anthropic:ApiKey"]);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var request = new AnthropicRequest(
            Model: "claude-haiku-4-5-20251001",
            MaxTokens: 1024,
            System: systemPrompt,
            Messages: history.Select(h => new AnthropicMsg(h.role, h.content)).ToList());

        var response = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>();
        return result?.Content.FirstOrDefault()?.Text ?? "Не удалось получить ответ";
    }

    private record AnthropicRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        string System,
        List<AnthropicMsg> Messages);

    private record AnthropicMsg(string Role, string Content);

    private record AnthropicResponse(List<AnthropicContent> Content);

    private record AnthropicContent(string Type, string Text);
}
