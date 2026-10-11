using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BotBase.Tests;

// Главная гарантия для обновлений: база в состоянии текущего прода переживает все новые миграции без потерь.
[Collection("db")]
public class MigrationUpgradeTests(PostgresFixture pg)
{
    // Последняя миграция, которая сейчас применена на проде (Railway).
    const string ProductionMigration = "20260819080045_AddOwnerNotificationChatId";

    AppDbContext Db(string cs) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);

    [Fact]
    public async Task Production_data_survives_all_new_migrations()
    {
        var cs = pg.NewDatabase();
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        await using (var db = Db(cs))
        {
            await db.GetService<IMigrator>().MigrateAsync(ProductionMigration);
            db.Businesses.Add(new Business
            {
                Id = businessId, Email = "salon@test.am", PasswordHash = BCrypt.Net.BCrypt.HashPassword("x"),
                BusinessName = "Салон", BotToken = "123:abc", OwnerNotificationChatId = 42, CrmWebhookUrl = "https://crm.test/hook"
            });
            db.Procedures.Add(new Procedure { Id = Guid.NewGuid(), BusinessId = businessId, Name = "Маникюр", DurationMinutes = 60, Price = 2500 });
            db.WorkSchedules.Add(new WorkSchedule { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = 0, IsWorkingDay = true, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0) });
            db.KnowledgeChunks.Add(new KnowledgeChunk { Id = Guid.NewGuid(), BusinessId = businessId, FileName = "prices.pdf", ExtractedText = "Прайс" });
            db.Conversations.Add(new Conversation { Id = conversationId, BusinessId = businessId, TelegramChatId = 777 });
            db.Messages.Add(new Message { Id = Guid.NewGuid(), ConversationId = conversationId, Role = "user", Content = "Здравствуйте" });
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), BusinessId = businessId, ConversationId = conversationId, ProcedureName = "Маникюр",
                ClientName = "Анна", ClientPhone = "+374", ScheduledAt = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), DurationMinutes = 60
            });
            await db.SaveChangesAsync();
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
