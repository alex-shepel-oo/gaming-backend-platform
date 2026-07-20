using EconomyService.Domain;
using EconomyService.Inbox;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Persistence;

public sealed class EconomyDbContext(DbContextOptions<EconomyDbContext> options) : DbContext(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Balance> Balances => Set<Balance>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ConversionRate> ConversionRates => Set<ConversionRate>();
    public DbSet<ConversionRequest> ConversionRequests => Set<ConversionRequest>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<ProjectedEventCount> ProjectedEventCounts => Set<ProjectedEventCount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EconomyDbContext).Assembly);
    }
}
