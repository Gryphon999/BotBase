using BotBase.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace BotBase.Tests;

// Просьба 11.10.2026: владелец должен видеть, что именно он загрузил и что из этого понимает бот.
[Collection("db")]
public class KnowledgeViewTests(PostgresFixture pg)
{
    record Item(Guid Id, string FileName, int TextLength, bool Broken, string? DuplicateOf);
    record Details(Guid Id, string FileName, string Text, bool Broken);

    static async Task Upload(HttpClient api, string name, byte[] bytes)
    {
        using var file = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name } };
        (await api.PostAsync("api/knowledge", file)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Owner_sees_text_and_warnings_about_broken_and_duplicate_files()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var api = app.CreateClient();
        api.WithToken(await api.RegisterAsync("kb@salon.am", "Крио"));

        await Upload(api, "часть1.txt", Encoding.UTF8.GetBytes("Криолиполиз: 4 манипулы, 30 000 драм."));
        await Upload(api, "всё.txt", Encoding.UTF8.GetBytes("Криолиполиз: 4 манипулы, 30 000 драм.\nINDIBA: 448 кГц."));
        await Upload(api, "сломан.txt", Encoding.UTF8.GetBytes(new string('�', 50) + " ok"));

        var list = (await api.GetFromJsonAsync<List<Item>>("api/knowledge"))!;
        Assert.Equal("всё.txt", list.Single(x => x.FileName == "часть1.txt").DuplicateOf);
        Assert.Null(list.Single(x => x.FileName == "всё.txt").DuplicateOf);
        Assert.True(list.Single(x => x.FileName == "сломан.txt").Broken);
        Assert.False(list.Single(x => x.FileName == "всё.txt").Broken);

        var full = await api.GetFromJsonAsync<Details>($"api/knowledge/{list.Single(x => x.FileName == "всё.txt").Id}");
        Assert.Contains("INDIBA: 448 кГц.", full!.Text);

        // Чужую базу знаний открыть нельзя.
        var other = app.CreateClient();
        other.WithToken(await other.RegisterAsync("other@salon.am", "Другой"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/knowledge/{full.Id}")).StatusCode);
    }
}
