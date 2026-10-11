using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Services;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace BotBase.Tests;

// CRM-лайт (октябрь 2026): ручная запись, напоминания, база клиентов, перенос/отмена, мастера, отзывы и «давно не были», статистика.
[Collection("db")]
public class CrmLiteTests(PostgresFixture pg)
{
    const long Owner = 1, Anna = 101, Boris = 102, Stranger = 999;

    record ProcedureDto(Guid Id, string Name, int DurationMinutes, decimal Price);
    record ClientItem(Guid Id, string Name, string Phone, bool HasTelegram, int Visits, DateTime? LastVisit, DateTime? NextVisit, decimal TotalSpent, string? Notes);
    record ApptDto(Guid Id, string ProcedureName, string ClientName, DateTime ScheduledAt, string Status, Guid? MasterId, string? MasterName,
        Guid? ClientId, string Source, decimal? Price, DateTime? ReminderSentAt, DateTime? ClientConfirmedAt, bool NoShow, int? Rating, bool HasTelegram);
    record ConflictDto(string Error, List<string> FreeTimes);
    record IdDto(Guid Id);

    sealed class Salon(BotApiFactory app, HttpClient api, Guid id, ProcedureDto manicure, ProcedureDto massage) : IDisposable
    {
        public BotApiFactory App => app;
        public HttpClient Api => api;
        public Guid Id => id;
        public ProcedureDto Manicure => manicure;
        public ProcedureDto Massage => massage;
        public HttpClient Http { get; } = app.CreateClient();
        public void Dispose() => app.Dispose();

        public async Task<T> Db<T>(Func<AppDbContext, Task<T>> f)
        {
            using var scope = app.Services.CreateScope();
            return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        public Task Webhook(object update) => Http.PostAsJsonAsync($"webhook/{id}", update);
        public Task Say(long chat, string text) => Webhook(new { update_id = Random.Shared.Next(), message = new { message_id = Random.Shared.Next(), date = 1760000000, chat = new { id = chat, type = "private" }, text } });
        public Task Press(long chat, string data) => Webhook(new
        {
            update_id = Random.Shared.Next(),
            callback_query = new { id = "cb" + Random.Shared.Next(), from = new { id = chat, is_bot = false, first_name = "K" }, chat_instance = "x", data,
                message = new { message_id = 5, date = 1760000000, chat = new { id = chat, type = "private" }, text = "…" } }
        });

        public DateTime Now => BusinessClock.Now(BusinessClock.DefaultZone);
        public DateTime Tomorrow(int hour) => Now.Date.AddDays(1).AddHours(hour);
    }

    async Task<Salon> SetupAsync()
    {
        var app = new BotApiFactory(pg.NewDatabase());
        var api = app.CreateClient().WithToken(await app.CreateClient().RegisterAsync("crm@salon.am", "Салон Лия"));
        (await api.PostAsJsonAsync("api/bots", new { botToken = "123:fake" })).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync("api/schedule", Enumerable.Range(0, 7).Select(d => new { dayOfWeek = d, isWorkingDay = true, startTime = "09:00", endTime = "20:00" }))).EnsureSuccessStatusCode();
        var manicure = await (await api.PostAsJsonAsync("api/procedures", new { name = "Маникюр", durationMinutes = 60, price = 8000 })).Content.ReadFromJsonAsync<ProcedureDto>();
        var massage = await (await api.PostAsJsonAsync("api/procedures", new { name = "Массаж", durationMinutes = 90, price = 15000 })).Content.ReadFromJsonAsync<ProcedureDto>();
        Guid id;
        using (var scope = app.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Businesses.SingleAsync()).Id;
        var salon = new Salon(app, api, id, manicure!, massage!);
        await salon.Say(Owner, "/start");
        return salon;
    }

    static string BookMarker(string name, string phone, DateTime at, string procedure = "Маникюр", int minutes = 60, string? master = null) =>
        $"{name}, вы записаны!\n[[BOOK:{{\"procedure_name\":\"{procedure}\",\"scheduled_at\":\"{at:yyyy-MM-ddTHH:mm:ss}\",\"duration_minutes\":{minutes}," +
        $"\"client_name\":\"{name}\",\"client_phone\":\"{phone}\"{(master is null ? "" : $",\"master_name\":\"{master}\"")}}}]]";

    async Task<List<ApptDto>> Appointments(Salon s) => (await s.Api.GetFromJsonAsync<List<ApptDto>>("api/appointments"))!;

    // ---------- 1. Ручная запись ----------

    [Fact]
    public async Task Manual_booking_is_checked_against_bot_bookings_and_working_hours()
    {
        using var s = await SetupAsync();
        s.App.Ai.Respond = (_, _) => BookMarker("Анна", "+374 91 111111", s.Tomorrow(11));
        await s.Say(Anna, "Запишите на маникюр завтра в 11");
        await s.App.Telegram.WaitFor(Anna);

        // Клиент позвонил и просит то же время — кабинет не даст занять его второй раз и предложит свободное.
        var conflict = await s.Api.PostAsJsonAsync("api/appointments/manual",
            new { clientName = "Борис", clientPhone = "+374 93 222222", procedureId = s.Manicure.Id, scheduledAt = s.Tomorrow(11).AddMinutes(30) });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = await conflict.Content.ReadFromJsonAsync<ConflictDto>();
        Assert.Contains("занято", body!.Error);
        Assert.Contains("12:00", body.FreeTimes);

        // Вне рабочего времени — только с явной галочкой.
        var late = await s.Api.PostAsJsonAsync("api/appointments/manual",
            new { clientName = "Борис", clientPhone = "+374 93 222222", procedureId = s.Manicure.Id, scheduledAt = s.Tomorrow(21) });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        (await s.Api.PostAsJsonAsync("api/appointments/manual",
            new { clientName = "Борис", clientPhone = "+374 93 222222", procedureId = s.Manicure.Id, scheduledAt = s.Tomorrow(21), ignoreWorkingHours = true })).EnsureSuccessStatusCode();

        var ok = await s.Api.PostAsJsonAsync("api/appointments/manual",
            new { clientName = "Борис", clientPhone = "+374 93 222222", procedureId = s.Massage.Id, scheduledAt = s.Tomorrow(14) });
        ok.EnsureSuccessStatusCode();

        var list = await Appointments(s);
        var manual = list.Single(a => a.ScheduledAt == s.Tomorrow(14));
        Assert.Equal(("Manual", "Confirmed", 15000m, "Массаж"), (manual.Source, manual.Status, manual.Price!.Value, manual.ProcedureName));
        Assert.Equal("Bot", list.Single(a => a.ClientName == "Анна").Source);
        Assert.Equal(8000m, list.Single(a => a.ClientName == "Анна").Price); // цену бот-записи взяли из прайса
    }

    // ---------- 3. База клиентов ----------

    [Fact]
    public async Task Clients_are_collected_by_phone_from_bot_and_manual_bookings()
    {
        using var s = await SetupAsync();
        s.App.Ai.Respond = (_, _) => BookMarker("Анна", "+374 (91) 11-11-11", s.Tomorrow(10));
        await s.Say(Anna, "Запишите");
        await s.App.Telegram.WaitFor(Anna);
        // Прошлый визит той же Анны (номер записан иначе) — владелец вносит задним числом.
        (await s.Api.PostAsJsonAsync("api/appointments/manual",
            new { clientName = "Анна Петросян", clientPhone = "37491111111", procedureId = s.Massage.Id, scheduledAt = s.Now.Date.AddDays(-10).AddHours(12) })).EnsureSuccessStatusCode();

        var clients = (await s.Api.GetFromJsonAsync<List<ClientItem>>("api/clients"))!;
        var anna = Assert.Single(clients);
        Assert.Equal((1, 15000m, true), (anna.Visits, anna.TotalSpent, anna.HasTelegram));
        Assert.Equal(s.Tomorrow(10), anna.NextVisit);

        (await s.Api.PutAsJsonAsync($"api/clients/{anna.Id}", new { name = "Анна Петросян", phone = anna.Phone, notes = "Аллергия на латекс" })).EnsureSuccessStatusCode();
        var found = (await s.Api.GetFromJsonAsync<List<ClientItem>>("api/clients?search=91111"))!;
        Assert.Equal("Аллергия на латекс", Assert.Single(found).Notes);
        Assert.Empty((await s.Api.GetFromJsonAsync<List<ClientItem>>("api/clients?search=Борис"))!);

        var history = (await s.Api.GetFromJsonAsync<List<ApptDto>>($"api/appointments?clientId={anna.Id}"))!;
        Assert.Equal(2, history.Count);
    }

    // ---------- 4. Перенос и отмена с уведомлением клиента ----------

    [Fact]
    public async Task Owner_reschedules_and_cancels_and_client_is_told_in_telegram()
    {
        using var s = await SetupAsync();
        s.App.Ai.Respond = (_, _) => BookMarker("Анна", "+374 91 111111", s.Tomorrow(10));
        await s.Say(Anna, "Запишите");
        await s.App.Telegram.WaitFor(Anna);
        s.App.Ai.Respond = (_, _) => BookMarker("Борис", "+374 93 222222", s.Tomorrow(15));
        await s.Say(Boris, "Запишите");
        await s.App.Telegram.WaitFor(Boris);
        var anna = (await Appointments(s)).Single(a => a.ClientName == "Анна");

        // На занятое время перенести нельзя.
        var busy = await s.Api.PutAsJsonAsync($"api/appointments/{anna.Id}/reschedule", new { scheduledAt = s.Tomorrow(15) });
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);

        (await s.Api.PutAsJsonAsync($"api/appointments/{anna.Id}/reschedule", new { scheduledAt = s.Tomorrow(17) })).EnsureSuccessStatusCode();
        Assert.Contains(s.App.Telegram.To(Anna), m => m.Contains("перенесена") && m.Contains("17:00"));
        Assert.Equal(s.Tomorrow(17), (await Appointments(s)).Single(a => a.Id == anna.Id).ScheduledAt);

        (await s.Api.PatchAsJsonAsync($"api/appointments/{anna.Id}/status", new { status = "Cancelled", notifyClient = true })).EnsureSuccessStatusCode();
        Assert.Contains(s.App.Telegram.To(Anna), m => m.Contains("отменена"));

        // CRM-интеграция меняет статус без notifyClient — клиенту ничего не уходит (как раньше).
        var boris = (await Appointments(s)).Single(a => a.ClientName == "Борис");
        var before = s.App.Telegram.To(Boris).Count;
        (await s.Api.PatchAsJsonAsync($"api/appointments/{boris.Id}/status", new { status = "Cancelled" })).EnsureSuccessStatusCode();
        Assert.Equal(before, s.App.Telegram.To(Boris).Count);

        // Сообщения кабинета сохраняются в разговор — ИИ увидит, на что отвечает клиент.
        var saved = await s.Db(db => db.Messages.Where(m => m.Role == "assistant").Select(m => m.Content).ToListAsync());
        Assert.Contains(saved, m => m.Contains("перенесена"));
    }

    // ---------- 5. Мастера ----------

    [Fact]
    public async Task Two_masters_take_clients_at_the_same_time_and_only_for_their_procedures()
    {
        using var s = await SetupAsync();
        (await s.Api.PostAsJsonAsync("api/masters", new { name = "Лиана", procedureIds = new[] { s.Manicure.Id } })).EnsureSuccessStatusCode();
        // У Армена своё расписание: только с 12:00.
        (await s.Api.PostAsJsonAsync("api/masters", new
        {
            name = "Армен", procedureIds = Array.Empty<Guid>(),
            schedule = Enumerable.Range(0, 7).Select(d => new { dayOfWeek = d, isWorkingDay = true, startTime = "12:00", endTime = "20:00" })
        })).EnsureSuccessStatusCode();

        // Сохранение общего расписания не трогает личное расписание мастера.
        (await s.Api.PutAsJsonAsync("api/schedule", Enumerable.Range(0, 7).Select(d => new { dayOfWeek = d, isWorkingDay = true, startTime = "09:00", endTime = "20:00" }))).EnsureSuccessStatusCode();
        Assert.Equal(7, await s.Db(db => db.WorkSchedules.CountAsync(w => w.MasterId != null)));

        async Task<string> Book(long chat, string name, string phone, DateTime at, string procedure = "Маникюр", int min = 60, string? master = null)
        {
            s.App.Ai.Respond = (_, _) => BookMarker(name, phone, at, procedure, min, master);
            var before = s.App.Telegram.To(chat).Count;
            await s.Say(chat, "Запишите");
            for (var i = 0; i < 200 && s.App.Telegram.To(chat).Count == before; i++) await Task.Delay(50);
            return s.App.Telegram.To(chat).Last();
        }

        // 12:00: два мастера — два клиента в одно время.
        Assert.Contains("записаны", await Book(201, "Ани", "+374 1", s.Tomorrow(12)));
        Assert.Contains("записаны", await Book(202, "Гаяне", "+374 2", s.Tomorrow(12)));
        Assert.Contains("занято", await Book(203, "Карен", "+374 3", s.Tomorrow(12)));
        // 10:00 Армен ещё не работает, Лиана свободна, но массаж она не делает.
        Assert.DoesNotContain("записаны", await Book(204, "Нарек", "+374 4", s.Tomorrow(10), "Массаж", 90));
        // Клиент просит конкретного мастера.
        Assert.Contains("записаны", await Book(205, "Мане", "+374 5", s.Tomorrow(15), master: "Армен"));

        var list = await Appointments(s);
        Assert.Equal(["Армен", "Лиана"], list.Where(a => a.ScheduledAt == s.Tomorrow(12)).Select(a => a.MasterName!).Order());
        Assert.Equal("Армен", list.Single(a => a.ClientName == "Мане").MasterName);
        Assert.Contains("Мастер: Армен", s.App.Telegram.To(Owner).Last());

        // Мастера видит ИИ в подсказке.
        Assert.Contains("МАСТЕРА", s.App.Ai.Prompts.Last());
    }

    // ---------- 2. Напоминания ----------

    async Task<Appointment> InsertAsync(Salon s, string name, string phone, long chat, DateTime at, DateTime createdUtc, bool reviewDone = true)
    {
        return await s.Db(async db =>
        {
            var client = new Client { Id = Guid.NewGuid(), BusinessId = s.Id, Name = name, Phone = phone, PhoneKey = Client.KeyOf(phone), TelegramChatId = chat };
            var appt = new Appointment
            {
                Id = Guid.NewGuid(), BusinessId = s.Id, ClientId = client.Id, ProcedureName = "Маникюр", ClientName = name, ClientPhone = phone,
                ScheduledAt = BusinessClock.AsStored(at), DurationMinutes = 60, Price = 8000, CreatedAt = createdUtc,
                ReviewRequestedAt = reviewDone ? DateTime.UtcNow : null
            };
            db.Clients.Add(client);
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            return appt;
        });
    }

    [Fact]
    public async Task Reminder_a_day_before_with_confirm_and_cancel_buttons()
    {
        using var s = await SetupAsync();
        var annaAppt = await InsertAsync(s, "Анна", "+37491111111", Anna, s.Now.AddHours(20), DateTime.UtcNow.AddDays(-2));
        var borisAppt = await InsertAsync(s, "Борис", "+37493222222", Boris, s.Now.AddHours(22), DateTime.UtcNow.AddDays(-2));
        // Записался час назад на через 5 часов — напоминание не нужно.
        await InsertAsync(s, "Карен", "+37494333333", 103, s.Now.AddHours(5), DateTime.UtcNow.AddHours(-1));

        var reminder = await s.App.Telegram.WaitFor(Anna, m => m.Contains("Напоминаем"));
        Assert.Contains("Маникюр", reminder);
        await s.App.Telegram.WaitFor(Boris, m => m.Contains("Напоминаем"));
        await Task.Delay(1500);
        Assert.Empty(s.App.Telegram.To(103));
        Assert.Single(s.App.Telegram.To(Anna), m => m.Contains("Напоминаем")); // не повторяется каждую минуту

        var buttons = s.App.Telegram.Buttons.Single(b => b.ChatId == Anna).Data;
        // Чужой человек не может нажать кнопку Анны.
        await s.Press(Stranger, buttons[1]);
        Assert.Equal(AppointmentStatus.Pending, await s.Db(db => db.Appointments.Where(a => a.Id == annaAppt.Id).Select(a => a.Status).SingleAsync()));

        await s.Press(Anna, buttons[0]);
        Assert.Contains("Ждём вас", await s.App.Telegram.WaitFor(Anna, m => m.Contains("Ждём")));
        var a = await s.Db(db => db.Appointments.SingleAsync(x => x.Id == annaAppt.Id));
        Assert.Equal(AppointmentStatus.Confirmed, a.Status);
        Assert.NotNull(a.ClientConfirmedAt);
        Assert.Contains(s.App.Telegram.To(Owner), m => m.Contains("Анна подтвердил"));

        await s.Press(Boris, s.App.Telegram.Buttons.Single(b => b.ChatId == Boris).Data[1]);
        await s.App.Telegram.WaitFor(Boris, m => m.Contains("Запись отменена"));
        Assert.Equal(AppointmentStatus.Cancelled, await s.Db(db => db.Appointments.Where(x => x.Id == borisAppt.Id).Select(x => x.Status).SingleAsync()));
        Assert.Contains(s.App.Telegram.To(Owner), m => m.Contains("Борис отменил") && m.Contains("освободилось"));
    }

    [Fact]
    public async Task Reminders_can_be_turned_off()
    {
        using var s = await SetupAsync();
        (await s.Api.PutAsJsonAsync("api/automation", new { timeZone = "Asia/Yerevan", remindersEnabled = false, reviewRequestsEnabled = false, winbackEnabled = false, winbackAfterDays = 45 })).EnsureSuccessStatusCode();
        await InsertAsync(s, "Анна", "+37491111111", Anna, s.Now.AddHours(20), DateTime.UtcNow.AddDays(-2));
        await Task.Delay(2500);
        Assert.Empty(s.App.Telegram.To(Anna));
    }

    // ---------- 6. Отзывы и «давно не были» ----------

    [Fact]
    public async Task Review_request_after_visit_and_low_rating_alerts_owner()
    {
        using var s = await SetupAsync();
        (await s.Api.PutAsJsonAsync("api/automation", new { timeZone = "Asia/Yerevan", remindersEnabled = true, reviewRequestsEnabled = true,
            reviewUrl = "https://g.page/salon-liya/review", winbackEnabled = false, winbackAfterDays = 45 })).EnsureSuccessStatusCode();
        var anna = await InsertAsync(s, "Анна", "+37491111111", Anna, s.Now.AddHours(-4), DateTime.UtcNow.AddDays(-3), reviewDone: false);
        await InsertAsync(s, "Борис", "+37493222222", Boris, s.Now.AddHours(-5), DateTime.UtcNow.AddDays(-3), reviewDone: false);

        await s.App.Telegram.WaitFor(Anna, m => m.Contains("Оцените"));
        var annaButtons = s.App.Telegram.Buttons.Single(b => b.ChatId == Anna).Data;
        Assert.Equal(5, annaButtons.Count);
        await s.Press(Anna, annaButtons[4]); // 5 ⭐
        Assert.Contains("g.page/salon-liya", await s.App.Telegram.WaitFor(Anna, m => m.Contains("Спасибо за высокую")));
        Assert.Equal(5, await s.Db(db => db.Appointments.Where(a => a.Id == anna.Id).Select(a => a.Rating).SingleAsync()));

        await s.App.Telegram.WaitFor(Boris, m => m.Contains("Оцените"));
        await s.Press(Boris, s.App.Telegram.Buttons.Single(b => b.ChatId == Boris).Data[1]); // 2 ⭐
        await s.App.Telegram.WaitFor(Owner, m => m.Contains("на 2 из 5"));
    }

    [Fact]
    public async Task Winback_message_once_for_clients_who_have_not_come_for_long()
    {
        using var s = await SetupAsync();
        await InsertAsync(s, "Анна", "+37491111111", Anna, s.Now.AddDays(-60), DateTime.UtcNow.AddDays(-61));
        await InsertAsync(s, "Борис", "+37493222222", Boris, s.Now.AddDays(-10), DateTime.UtcNow.AddDays(-11));
        (await s.Api.PutAsJsonAsync("api/automation", new { timeZone = "Asia/Yerevan", remindersEnabled = true, reviewRequestsEnabled = false,
            winbackEnabled = true, winbackAfterDays = 45 })).EnsureSuccessStatusCode();

        await s.App.Telegram.WaitFor(Anna, m => m.Contains("Давно не виделись"));
        await Task.Delay(2500);
        Assert.Single(s.App.Telegram.To(Anna));
        Assert.Empty(s.App.Telegram.To(Boris));

        var bad = await s.Api.PutAsJsonAsync("api/automation", new { timeZone = "Mars/Olympus", remindersEnabled = true, reviewRequestsEnabled = false, winbackEnabled = true, winbackAfterDays = 45 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ---------- 7. Статистика ----------

    record Stats(int Total, int ByBot, int ByManual, int Cancelled, int NoShows, int Visits, decimal Revenue, int NewClients, List<TopItem> TopProcedures);
    record TopItem(string Name, int Count, decimal Revenue);

    [Fact]
    public async Task Stats_count_bot_bookings_revenue_and_no_shows()
    {
        using var s = await SetupAsync();
        var month = s.Now.ToString("yyyy-MM");
        var monthStart = new DateTime(s.Now.Year, s.Now.Month, 1);
        // Два прошлых визита в этом месяце (если сегодня 1-е число — ставим их на сегодня, раньше «сейчас» на пару часов нельзя гарантировать).
        var past1 = s.Now.Day > 1 ? monthStart.AddHours(10) : s.Now.AddMinutes(-150);
        var past2 = s.Now.Day > 1 ? monthStart.AddHours(13) : s.Now.AddMinutes(-90);
        await InsertAsync(s, "Анна", "+37491111111", Anna, past1, DateTime.UtcNow.AddDays(-40));
        var noShow = await InsertAsync(s, "Борис", "+37493222222", Boris, past2, DateTime.UtcNow.AddDays(-40));
        (await s.Api.PatchAsJsonAsync($"api/appointments/{noShow.Id}/no-show", new { noShow = true })).EnsureSuccessStatusCode();

        var stats = (await s.Api.GetFromJsonAsync<Stats>($"api/stats?month={month}"))!;
        Assert.Equal((2, 2, 0, 1, 1, 8000m), (stats.Total, stats.ByBot, stats.ByManual, stats.NoShows, stats.Visits, stats.Revenue));
        Assert.Equal(2, stats.NewClients);
        Assert.Equal("Маникюр", stats.TopProcedures[0].Name);
    }

    // ---------- Часовой пояс ----------

    [Fact]
    public async Task Bot_uses_business_time_not_server_utc()
    {
        using var s = await SetupAsync();
        // Час назад по Еревану — для сервера в UTC это «через 3 часа». Раньше бот принимал такую запись.
        var hourAgo = s.Now.AddHours(-1);
        s.App.Ai.Respond = (_, _) => BookMarker("Анна", "+374 91 111111", hourAgo);
        await s.Say(Anna, "Запишите");
        Assert.Contains("уже прошло", await s.App.Telegram.WaitFor(Anna));
        Assert.Contains($"сейчас {s.Now:HH:}", s.App.Ai.Prompts.Last());
    }
}
