using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(l => l.Id).HasName("pk_ledger_entries");

        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(l => l.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(l => l.CurrencyId).HasColumnName("currency_id").IsRequired();
        builder.Property(l => l.Amount).HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(l => l.TransactionType).HasColumnName("transaction_type").HasConversion<short>();
        builder.Property(l => l.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(l => l.Reason).HasColumnName("reason");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");

        builder.HasOne(l => l.Currency)
            .WithMany()
            .HasForeignKey(l => l.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ledger_entries_currencies_currency_id");

        // Backs mutation idempotency: a retried request carries the same key,
        // and the unique index is what turns a duplicate insert into a
        // conflict the caller replays from instead of double-posting.
        builder.HasIndex(l => l.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ix_ledger_entries_idempotency_key");

        builder.HasIndex(l => new { l.UserId, l.CurrencyId })
            .HasDatabaseName("ix_ledger_entries_user_id_currency_id");

        builder.HasIndex(l => l.CurrencyId).HasDatabaseName("ix_ledger_entries_currency_id");
    }
}
