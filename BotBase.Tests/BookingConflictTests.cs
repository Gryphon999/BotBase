using BotBase.Api.Data;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace BotBase.Tests;

// Баг 11.10.2026 (КриоЭксперт): бот сказал второму клиенту «вы записаны» на уже занятое время.
[Collection("db")]
public class BookingConflictTests(PostgresFixture pg)
{
    const long OwnerChat = 1, Anna = 101, Viktor = 102;

    // Понедельник через 1-2 недели, 11:00 — дальше «окна» в 7 дней, как в реальном случае.
    static readonly DateTime Slot = NextMonday(DateTime.Today.AddDays(9)).AddHours(11);

    static DateTime NextMonday(DateTime from)
    {
        while (from.DayOfWeek != DayOfWeek.Monday) from = from.AddDays(1);
        return from.Date;
    }

    static string BookingReply(string name, DateTime at, int minutes = 60) =>
        $"{name}, отлично! Вы успешно записаны на {at:dd.MM} в {at:HH:mm}.\n" +
        $"[[BOOK:{{\"procedure_name\":\"Криолиполиз 6 манипул\",\"scheduled_at\":\"{at:yyyy-MM-ddTHH:mm:ss}\",\"duration_minutes\":{minutes},\"client_name\":\"{name}\",\"client_phone\":\"+37400000000\"}}]]";

    static object Update(long chatId, string text) => new
    {
        update_id = Random.Shared.Next(),
        message = new { message_id = Random.Shared.Next(), date = 1760000000, chat = new { id = chatId, type = "private" }, text }
    };

    async Task<(BotApiFactory app, HttpClient client, Guid businessId)> SetupAsync()
    {
        var app = new BotApiFactory(pg.NewDatabase());
        var client = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("crio@test.am", "КриоЭксперт"));
        (await client.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = (await db.Businesses.SingleAsync()).Id;
        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(OwnerChat, "/start")); // владелец
        return (app, client, id);
    }

    async Task<int> CountAppointments(BotApiFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Appointments.CountAsync();
    }

    [Fact]
    public async Task Second_client_is_not_told_booked_for_a_taken_slot()
    {
        var (app, _, id) = await SetupAsync();
        using var _app = app;
        var http = app.CreateClient();

        app.Ai.Respond = (_, _) => BookingReply("Анна", Slot);
        await http.PostAsJsonAsync($"webhook/{id}", Update(Anna, "Криолиполиз, 6 манипул"));

        app.Ai.Respond = (_, _) => BookingReply("Виктор", Slot);
        await http.PostAsJsonAsync($"webhook/{id}", Update(Viktor, "6"));

        Assert.Equal(1, await CountAppointments(app));

        var toViktor = app.Telegram.To(Viktor).Last();
        Assert.DoesNotContain("записаны", toViktor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("занято", toViktor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12:00", toViktor); // предлагаем ближайшее свободное время

        // Владелец получил уведомление только об одной записи.
        Assert.Single(app.Telegram.To(OwnerChat), m => m.Contains("Новая запись"));
    }

    [Fact]
    public async Task Ai_sees_bookings_beyond_one_week()
    {
        var (app, _, id) = await SetupAsync();
        using var _app = app;
        var http = app.CreateClient();

        app.Ai.Respond = (_, _) => BookingReply("Анна", Slot);
        await http.PostAsJsonAsync($"webhook/{id}", Update(Anna, "Записаться"));

        app.Ai.Prompts.Clear();
        app.Ai.Respond = (_, _) => "На какое время вас записать?";
        await http.PostAsJsonAsync($"webhook/{id}", Update(Viktor, "Хочу записаться"));

        Assert.Contains(app.Ai.Prompts, p => p.Contains(Slot.ToString("dd.MM.yyyy HH:mm")));
    }

    [Fact]
    public async Task Simultaneous_requests_create_only_one_booking()
    {
        var (app, _, id) = await SetupAsync();
        using var _app = app;

        app.Ai.Respond = (_, history) => BookingReply("Клиент", Slot);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(200 + i, "6"))));

        Assert.Equal(1, await CountAppointments(app));
        Assert.Equal(5, Enumerable.Range(0, 6).Count(i => app.Telegram.To(200 + i).Last().Contains("занято", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Booking_outside_working_hours_is_not_confirmed()
    {
        var (app, _, id) = await SetupAsync();
        using var _app = app;

        app.Ai.Respond = (_, _) => BookingReply("Анна", Slot.Date.AddHours(20)); // рабочий день до 18:00
        await app.CreateClient().PostAsJsonAsync($"webhook/{id}", Update(Anna, "в 20:00"));

        Assert.Equal(0, await CountAppointments(app));
        var reply = app.Telegram.To(Anna).Last();
        Assert.DoesNotContain("записаны", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("09:00", reply); // называем часы работы
    }
}
