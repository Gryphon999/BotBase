using BotBase.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace BotBase.Tests;

// Имитация выкладки обновления: один экземпляр приложения останавливается, новый стартует на той же базе.
[Collection("db")]
public class PersistenceAcrossDeployTests(PostgresFixture pg)
{
    [Fact]
    public async Task Client_data_and_session_survive_redeploy()
    {
        var cs = pg.NewDatabase();
        string token;

        using (var before = new ApiFactory(cs))
        {
            var api = before.CreateClient();
            token = await api.RegisterAsync("owner@salon.am", "Студия Демо");
            api.WithToken(token);

            (await api.PostAsJsonAsync("api/procedures", new { name = "Маникюр", durationMinutes = 60, price = 2500 })).EnsureSuccessStatusCode();
            (await api.PutAsJsonAsync("api/schedule", Enumerable.Range(0, 7).Select(d => new
            {
                dayOfWeek = d, isWorkingDay = d < 6, startTime = d < 6 ? "10:00" : null, endTime = d < 6 ? "20:00" : null
            }))).EnsureSuccessStatusCode();

            using var file = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Адрес: Пушкина 10")), "file", "info.txt" } };
            (await api.PostAsync("api/knowledge", file)).EnsureSuccessStatusCode();
            (await api.PostAsJsonAsync("api/support/tickets", new { subject = "Вопрос", text = "Как подключить бота?" })).EnsureSuccessStatusCode();
        }

        using var after = new ApiFactory(cs);
        var client = after.CreateClient();

        // Старый токен (лежит у клиента в браузере) продолжает работать — пользователя не выкидывает.
        client.WithToken(token);
        var procedures = await client.GetFromJsonAsync<List<ProcedureDto>>("api/procedures");
        Assert.Equal("Маникюр", Assert.Single(procedures!).Name);

        var schedule = await client.GetFromJsonAsync<List<ScheduleDto>>("api/schedule");
        Assert.Equal(6, schedule!.Count(s => s.IsWorkingDay));
        Assert.Contains(schedule!, s => s.StartTime == "10:00");

        var knowledge = await client.GetFromJsonAsync<List<KnowledgeDto>>("api/knowledge");
        Assert.Equal("info.txt", Assert.Single(knowledge!).FileName);

        var tickets = await client.GetFromJsonAsync<List<TicketDto>>("api/support/tickets");
        Assert.Equal("Вопрос", Assert.Single(tickets!).Subject);

        // И повторный вход по паролю тоже.
        var login = await after.CreateClient().PostAsJsonAsync("api/auth/login", new { email = "owner@salon.am", password = "Pass-12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Admin_account_is_not_duplicated_or_reset_on_redeploy()
    {
        var cs = pg.NewDatabase();
        using (var first = new ApiFactory(cs)) await first.CreateClient().AdminLoginAsync();
        using var second = new ApiFactory(cs);
        var token = await second.CreateClient().AdminLoginAsync();
        Assert.False(string.IsNullOrEmpty(token));
    }

    record ProcedureDto(Guid Id, string Name);
    record ScheduleDto(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
    record KnowledgeDto(Guid Id, string FileName);
    record TicketDto(Guid Id, string Subject, string Status);
}
