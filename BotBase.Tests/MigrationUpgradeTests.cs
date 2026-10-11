using BotBase.Api.Data;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BotBase.Tests;

// Главная гарантия для обновлений: база в состоянии текущего прода переживает все новые миграции без потерь.
[Collection("db")]
public class MigrationUpgradeTests(PostgresFixture pg)
{
    // Последняя миграция, которая сейчас применена на проде (Railway). Обновлять после каждого деплоя с миграцией.
    const string ProductionMigration = "20261011110746_AddAdminAndSupport";

    AppDbContext Db(string cs) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);

    [Fact]
    public async Task Production_data_survives_all_new_migrations()
    {
        var cs = pg.NewDatabase();
        var hash = BCrypt.Net.BCrypt.HashPassword("x");

        await using (var db = Db(cs))
        {
            await db.GetService<IMigrator>().MigrateAsync(ProductionMigration);
            // Данные вставляем обычным SQL под схему прода — текущая модель EF может быть уже новее.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Businesses" ("Id","Email","PasswordHash","BusinessName","BotToken","IsActive","CreatedAt","CrmWebhookUrl","OwnerNotificationChatId")
                VALUES ('11111111-1111-1111-1111-111111111111','salon@test.am',{hash},'Салон','123:abc',true,now(),'https://crm.test/hook',42);
                INSERT INTO "Procedures" ("Id","BusinessId","Name","DurationMinutes","Price","IsActive","CreatedAt")
                VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','Маникюр',60,2500,true,now());
                INSERT INTO "WorkSchedules" ("Id","BusinessId","DayOfWeek","IsWorkingDay","StartTime","EndTime")
                VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111',0,true,'09:00','18:00');
                INSERT INTO "KnowledgeChunks" ("Id","BusinessId","FileName","ExtractedText","UploadedAt")
                VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','prices.pdf','Прайс',now());
                INSERT INTO "Conversations" ("Id","BusinessId","TelegramChatId","StartedAt")
                VALUES ('22222222-2222-2222-2222-222222222222','11111111-1111-1111-1111-111111111111',777,now());
                INSERT INTO "Messages" ("Id","ConversationId","Role","Content","CreatedAt")
                VALUES (gen_random_uuid(),'22222222-2222-2222-2222-222222222222','user','Здравствуйте',now());
                INSERT INTO "Appointments" ("Id","BusinessId","ConversationId","ProcedureName","ClientName","ClientPhone","ScheduledAt","DurationMinutes","Status","CreatedAt","UpdatedAt")
                VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222','Маникюр','Анна','+374','2026-10-12 10:00+00',60,0,now(),now());
                INSERT INTO "SupportTickets" ("Id","BusinessId","Subject","Status","CreatedAt","UpdatedAt")
                VALUES ('33333333-3333-3333-3333-333333333333','11111111-1111-1111-1111-111111111111','Вопрос',0,now(),now());
                INSERT INTO "SupportMessages" ("Id","TicketId","FromAdmin","Text","CreatedAt")
                VALUES (gen_random_uuid(),'33333333-3333-3333-3333-333333333333',false,'Помогите',now());
                """);
        }

        await using (var db = Db(cs))
        {
            await db.Database.MigrateAsync(); // всё, что появилось после прода

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var b = await db.Businesses.SingleAsync();
            Assert.Equal(("salon@test.am", "Салон", "123:abc", 42L, true), (b.Email, b.BusinessName, b.BotToken, b.OwnerNotificationChatId!.Value, b.IsActive));
            Assert.Equal("https://crm.test/hook", b.CrmWebhookUrl);
            Assert.True(BCrypt.Net.BCrypt.Verify("x", b.PasswordHash));
            Assert.Equal(2500, (await db.Procedures.SingleAsync()).Price);
            Assert.Equal(new TimeOnly(18, 0), (await db.WorkSchedules.SingleAsync()).EndTime);
            Assert.Equal("Прайс", (await db.KnowledgeChunks.SingleAsync()).ExtractedText);
            Assert.Equal("Здравствуйте", (await db.Messages.SingleAsync()).Content);
            Assert.Equal("Анна", (await db.Appointments.SingleAsync()).ClientName);
            Assert.Equal("Помогите", (await db.SupportMessages.SingleAsync()).Text);
        }
    }

    [Fact]
    public void New_migrations_never_drop_or_alter_existing_data()
    {
        // Защита на будущее: если кто-то добавит миграцию, удаляющую таблицу/столбец, тест упадёт до деплоя.
        var migrationsDir = Path.Combine(FindRepoRoot(), "BotBase.Api", "Migrations");
        var newer = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs") && !f.EndsWith("ModelSnapshot.cs"))
            .Where(f => string.CompareOrdinal(Path.GetFileName(f), ProductionMigration) > 0);

        foreach (var file in newer)
        {
            var text = File.ReadAllText(file);
            var up = text[text.IndexOf("void Up(", StringComparison.Ordinal)..text.IndexOf("void Down(", StringComparison.Ordinal)];
            foreach (var danger in new[] { "DropTable", "DropColumn", "RenameColumn", "RenameTable", "AlterColumn", "DeleteData", "Sql(" })
                Assert.False(up.Contains(danger), $"{Path.GetFileName(file)}: Up() содержит {danger} — нужна проверка и бэкап перед деплоем");
        }
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BotBase.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("BotBase.slnx not found");
    }
}
