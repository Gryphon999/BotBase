using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BotBase.Api.Services;

// Автосообщения клиентам раз в AutomationIntervalSec:
//  • напоминание за сутки с кнопками «Приду / Отменить»;
//  • просьба оценить визит через 2 часа после него (если включено);
//  • «давно не были» клиентам без визитов WinbackAfterDays дней (если включено).
// Ночью (QuietHours) клиентам не пишем — сообщения уйдут утром.
public class AutomationWorker(IServiceScopeFactory scopes, IOptions<BotOptions> options, ILogger<AutomationWorker> log) : BackgroundService
{
    const int ReminderAheadHours = 24, ReminderMinLeadHours = 2, ReminderNotSoonerAfterBookingHours = 12;
    const int ReviewAfterHours = 2, ReviewWindowHours = 24, WinbackPerRun = 20;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogError(ex, "Automation run failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.AutomationIntervalSec)), ct); }
            catch (OperationCanceledException) { }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Businesses
            .Where(b => b.IsActive && b.BotToken != null && b.CrmEnabled && (b.RemindersEnabled || b.ReviewRequestsEnabled || b.WinbackEnabled))
            .Select(b => b.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await ProcessBusinessAsync(id, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogError(ex, "Automation failed for business {Id}", id); }
        }
    }

    async Task ProcessBusinessAsync(Guid businessId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messenger = scope.ServiceProvider.GetRequiredService<ClientMessenger>();
        var business = await db.Businesses.FirstAsync(b => b.Id == businessId, ct);
        var zone = BusinessClock.Zone(business.TimeZone);
        var now = BusinessClock.Now(business.TimeZone);
        var o = options.Value;
        // Автосообщения — часть тарифа «Бизнес»: без открытого доступа к CRM клиентам ничего не пишем.
        if (!business.HasCrm(now)) return;
        if (now.Hour < o.QuietHoursEnd || now.Hour >= o.QuietHoursStart) return;

        if (business.RemindersEnabled)
        {
            var due = await db.Appointments.Include(a => a.Master)
                .Where(a => a.BusinessId == businessId && a.Status != AppointmentStatus.Cancelled && a.ReminderSentAt == null
                         && a.ScheduledAt > now.AddHours(ReminderMinLeadHours) && a.ScheduledAt <= now.AddHours(ReminderAheadHours))
                .ToListAsync(ct);
            foreach (var a in due)
            {
                // Только что записался — напоминание сразу после записи выглядит странно; ждём хотя бы 12 часов.
                // Пример: запись в 22:00 на завтра 12:00 → напоминание утром в 10:00.
                var createdLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc), zone);
                if (now - createdLocal < TimeSpan.FromHours(ReminderNotSoonerAfterBookingHours)) continue;

                a.ReminderSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await messenger.SendAsync(business, a, l => ClientTexts.Reminder(l, business, a, now), l => ClientTexts.ReminderButtons(l, a));
            }
        }

        if (business.ReviewRequestsEnabled)
        {
            var since = now.AddHours(-ReviewWindowHours - 12);
            var candidates = await db.Appointments
                .Where(a => a.BusinessId == businessId && a.Status != AppointmentStatus.Cancelled && !a.NoShow && a.ReviewRequestedAt == null
                         && a.ScheduledAt >= since && a.ScheduledAt < now)
                .ToListAsync(ct);
            foreach (var a in candidates)
            {
                var end = a.ScheduledAt.AddMinutes(a.DurationMinutes);
                if (end > now.AddHours(-ReviewAfterHours) || end < now.AddHours(-ReviewWindowHours)) continue;
                a.ReviewRequestedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await messenger.SendAsync(business, a, l => ClientTexts.ReviewRequest(l, business), _ => ClientTexts.RatingButtons(a));
            }
        }

        if (business.WinbackEnabled)
        {
            var cutoff = now.AddDays(-business.WinbackAfterDays);
            var lapsed = await db.Clients
                .Where(c => c.BusinessId == businessId && c.TelegramChatId != null)
                .Select(c => new
                {
                    Client = c,
                    LastVisit = c.Appointments.Where(a => a.Status != AppointmentStatus.Cancelled && !a.NoShow && a.ScheduledAt < now)
                                              .Max(a => (DateTime?)a.ScheduledAt),
                    HasFuture = c.Appointments.Any(a => a.Status != AppointmentStatus.Cancelled && a.ScheduledAt >= now)
                })
                .Where(x => x.LastVisit != null && x.LastVisit < cutoff && !x.HasFuture
                         && (x.Client.WinbackSentAt == null || x.Client.WinbackSentAt < x.LastVisit))
                .Take(WinbackPerRun)
                .ToListAsync(ct);
            foreach (var x in lapsed)
            {
                // Одно сообщение на «перерыв»: после нового визита клиент снова может получить его через N дней.
                x.Client.WinbackSentAt = now;
                await db.SaveChangesAsync(ct);
                await messenger.SendAsync(business, x.Client.TelegramChatId!.Value, l => ClientTexts.Winback(l, business, x.Client));
            }
        }
    }
}
