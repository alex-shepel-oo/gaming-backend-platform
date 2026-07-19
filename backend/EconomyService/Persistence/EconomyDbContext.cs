using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Persistence;

public sealed class EconomyDbContext(DbContextOptions<EconomyDbContext> options) : DbContext(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Balance> Balances => Set<Balance>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ConversionRate> ConversionRates => Set<ConversionRate>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EconomyDbContext).Assembly);
    }
}
