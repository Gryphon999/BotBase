using BotBase.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace BotBase.Tests;

[Collection("db")]
public class AdminAndSupportTests(PostgresFixture pg)
{
    [Fact]
    public async Task Admin_endpoints_reject_business_tokens()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("a@a.am", "A"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/admin/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsJsonAsync("api/admin/auth/login", new { email = ApiFactory.AdminEmail, password = "wrong" })).StatusCode);
    }

    [Fact]
    public async Task Admin_sees_businesses_and_can_disable_enable_impersonate()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var bizToken = await app.CreateClient().RegisterAsync("salon@a.am", "Салон Роза");
        var admin = app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync());

        var list = await admin.GetFromJsonAsync<List<BusinessDto>>("api/admin/businesses");
        var biz = Assert.Single(list!);
        Assert.Equal(("Салон Роза", true), (biz.BusinessName, biz.IsActive));

        (await admin.PostAsync($"api/admin/businesses/{biz.Id}/disable", null)).EnsureSuccessStatusCode();
        var login = await app.CreateClient().PostAsJsonAsync("api/auth/login", new { email = "salon@a.am", password = "Pass-12345" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        // Уже выданный токен тоже перестаёт работать.
        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient().WithToken(bizToken).GetAsync("api/procedures")).StatusCode);

        // Админ может зайти в кабинет даже отключённого клиента (чтобы разобраться).
        var imp = await (await admin.PostAsync($"api/admin/businesses/{biz.Id}/impersonate", null)).Content.ReadFromJsonAsync<ImpersonateDto>();
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().WithToken(imp!.Token).GetAsync("api/procedures")).StatusCode);

        (await admin.PostAsync($"api/admin/businesses/{biz.Id}/enable", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().WithToken(bizToken).GetAsync("api/procedures")).StatusCode);
    }

    [Fact]
    public async Task Delete_requires_exact_name_and_removes_all_client_data()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("del@a.am", "Удаляемый"));
        (await client.PostAsJsonAsync("api/procedures", new { name = "Стрижка", durationMinutes = 30, price = 1000 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("api/support/tickets", new { subject = "S", text = "T" })).EnsureSuccessStatusCode();
        var keep = await app.CreateClient().RegisterAsync("keep@a.am", "Остаётся");

        var admin = app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync());
        var id = (await admin.GetFromJsonAsync<List<BusinessDto>>("api/admin/businesses"))!.Single(b => b.BusinessName == "Удаляемый").Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"api/admin/businesses/{id}?confirmName=Другое")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"api/admin/businesses/{id}?confirmName={Uri.EscapeDataString("Удаляемый")}")).StatusCode);

        var left = await admin.GetFromJsonAsync<List<BusinessDto>>("api/admin/businesses");
        Assert.Equal("Остаётся", Assert.Single(left!).BusinessName);
        Assert.Empty((await admin.GetFromJsonAsync<List<TicketDto>>("api/admin/support/tickets"))!);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().WithToken(keep).GetAsync("api/procedures")).StatusCode);
    }

    [Fact]
    public async Task Support_ticket_round_trip_and_settings()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("c@a.am", "Клиент"));
        var admin = app.CreateClient().WithToken(await app.CreateClient().AdminLoginAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("api/admin/settings", new { supportEmail = "not-an-email" })).StatusCode);
        (await admin.PutAsJsonAsync("api/admin/settings", new { supportEmail = "help@botbase.am", supportTelegramChatId = "12345" })).EnsureSuccessStatusCode();
        var settings = await admin.GetFromJsonAsync<SettingsDto>("api/admin/settings");
        Assert.Equal("help@botbase.am", settings!.SupportEmail);

        var created = await (await client.PostAsJsonAsync("api/support/tickets", new { subject = "Бот молчит", text = "Не отвечает с утра" })).Content.ReadFromJsonAsync<TicketDetailDto>();
        var ticket = Assert.Single((await admin.GetFromJsonAsync<List<TicketDto>>("api/admin/support/tickets"))!);
        Assert.Equal(("Бот молчит", "Open", "Клиент"), (ticket.Subject, ticket.Status, ticket.BusinessName));

        (await admin.PostAsJsonAsync($"api/admin/support/tickets/{ticket.Id}/reply", new { text = "Перезапустили бота, проверьте" })).EnsureSuccessStatusCode();
        var detail = await client.GetFromJsonAsync<TicketDetailDto>($"api/support/tickets/{created!.Id}");
        Assert.Equal("Answered", detail!.Status);
        Assert.Collection(detail.Messages, m => Assert.False(m.FromAdmin), m => Assert.True(m.FromAdmin));

        // Клиент отвечает в ту же переписку — обращение снова открыто.
        (await client.PostAsJsonAsync($"api/support/tickets/{created.Id}/messages", new { text = "Заработало, спасибо!" })).EnsureSuccessStatusCode();
        detail = await client.GetFromJsonAsync<TicketDetailDto>($"api/support/tickets/{created.Id}");
        Assert.Equal(("Open", 3), (detail!.Status, detail.Messages.Count));

        // Чужое обращение клиенту недоступно.
        var stranger = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("x@a.am", "Чужой"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/support/tickets/{created.Id}")).StatusCode);
    }

    record BusinessDto(Guid Id, string BusinessName, bool IsActive);
    record ImpersonateDto(string Token);
    record TicketDto(Guid Id, string Subject, string Status, string BusinessName);
    record TicketDetailDto(Guid Id, string Status, List<MessageDto> Messages);
    record MessageDto(bool FromAdmin, string Text);
    record SettingsDto(string? SupportEmail);
}
