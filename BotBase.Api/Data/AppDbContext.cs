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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>().HasIndex(a => a.Email).IsUnique();
        modelBuilder.Entity<PlatformSetting>().HasKey(s => s.Key);
        modelBuilder.Entity<SupportTicket>().HasIndex(t => new { t.BusinessId, t.UpdatedAt });
        modelBuilder.Entity<SupportTicket>()
            .HasMany(t => t.Messages).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
