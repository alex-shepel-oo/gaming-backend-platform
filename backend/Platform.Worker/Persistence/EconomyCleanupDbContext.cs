using Microsoft.EntityFrameworkCore;

namespace Platform.Worker.Persistence;

// Cleanup-only view of economy_db: just the outbox columns the retention
// sweep needs. Deliberately not the full EconomyDbContext model - the
// worker never touches balances or ledger entries.
public sealed class EconomyCleanupDbContext(DbContextOptions<EconomyCleanupDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessageRecord>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).HasColumnName("id");
            builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });
    }
}

public sealed class OutboxMessageRecord
{
    public Guid Id { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}
