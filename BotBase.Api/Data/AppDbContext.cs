using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Master> Masters => Set<Master>();
    public DbSet<MasterProcedure> MasterProcedures => Set<MasterProcedure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>().HasIndex(a => a.Email).IsUnique();
        modelBuilder.Entity<PlatformSetting>().HasKey(s => s.Key);
        modelBuilder.Entity<SupportTicket>().HasIndex(t => new { t.BusinessId, t.UpdatedAt });
        modelBuilder.Entity<SupportTicket>()
            .HasMany(t => t.Messages).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Client>().HasIndex(c => new { c.BusinessId, c.PhoneKey }).IsUnique();
        modelBuilder.Entity<Client>().Property(c => c.Name).HasMaxLength(200);
        modelBuilder.Entity<Client>().Property(c => c.Phone).HasMaxLength(50);
        modelBuilder.Entity<Client>().Property(c => c.PhoneKey).HasMaxLength(30);
        modelBuilder.Entity<Client>().Property(c => c.Notes).HasMaxLength(4000);

        modelBuilder.Entity<Master>().Property(m => m.Name).HasMaxLength(100);
        modelBuilder.Entity<MasterProcedure>().HasKey(mp => new { mp.MasterId, mp.ProcedureId });
        modelBuilder.Entity<MasterProcedure>().HasOne(mp => mp.Master).WithMany(m => m.Procedures).HasForeignKey(mp => mp.MasterId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MasterProcedure>().HasOne(mp => mp.Procedure).WithMany().HasForeignKey(mp => mp.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkSchedule>().HasOne<Master>().WithMany().HasForeignKey(s => s.MasterId).OnDelete(DeleteBehavior.Cascade);

        // Удаление клиента или мастера не удаляет историю записей.
        modelBuilder.Entity<Appointment>().HasOne(a => a.Client).WithMany(c => c.Appointments).HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Appointment>().HasOne(a => a.Master).WithMany().HasForeignKey(a => a.MasterId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Appointment>().HasIndex(a => new { a.BusinessId, a.ScheduledAt });
        modelBuilder.Entity<Appointment>().Property(a => a.Price).HasPrecision(12, 2);
        modelBuilder.Entity<Business>().Property(b => b.TimeZone).HasMaxLength(64);
    }
}
