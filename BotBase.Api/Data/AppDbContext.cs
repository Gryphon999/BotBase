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
}
