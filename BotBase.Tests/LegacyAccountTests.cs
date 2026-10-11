using BotBase.Api.Data;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace BotBase.Tests;

// Аккаунты, зарегистрированные до появления расписания (на проде — «ЛераМожетВсе»), не имеют строк WorkSchedules.
[Collection("db")]
public class LegacyAccountTests(PostgresFixture pg)
{
    [Fact]
    public async Task Account_without_schedule_sees_default_week_and_can_save_it()
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("legacy@salon.am", "Старый аккаунт"));
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().WorkSchedules.ExecuteDeleteAsync();

        var shown = await client.GetFromJsonAsync<List<Row>>("api/schedule");
        Assert.Equal(7, shown!.Count);
        Assert.Equal(5, shown.Count(r => r.IsWorkingDay));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("api/schedule", shown)).StatusCode);
        using (var scope = app.Services.CreateScope())
            Assert.Equal(7, await scope.ServiceProvider.GetRequiredService<AppDbContext>().WorkSchedules.CountAsync());
    }

    record Row(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
}

[Collection("db")]
public class RegistrationValidationTests(PostgresFixture pg)
{
    [Theory]
    [InlineData("test", "Pass-12345")]          // так на проде появился аккаунт, в который нельзя войти
    [InlineData("a@b", "Pass-12345")]
    [InlineData("ok@salon.am", "123")]          // слишком короткий пароль
    public async Task Invalid_email_or_short_password_is_rejected(string email, string password)
    {
        using var app = new ApiFactory(pg.NewDatabase());
        var resp = await app.CreateClient().PostAsJsonAsync("api/auth/register", new { email, password, businessName = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
