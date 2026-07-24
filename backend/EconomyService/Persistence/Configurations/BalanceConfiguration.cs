using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class BalanceConfiguration : IEntityTypeConfiguration<Balance>
{
    public void Configure(EntityTypeBuilder<Balance> builder)
    {
        builder.ToTable("balances", table => table.HasCheckConstraint(
            "ck_balances_amount_non_negative",
            "amount >= 0"));
        builder.HasKey(b => b.Id).HasName("pk_balances");

        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(b => b.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(b => b.CurrencyId).HasColumnName("currency_id").IsRequired();
        builder.Property(b => b.Amount).HasColumnName("amount").HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(b => b.Version).HasColumnName("version").HasDefaultValue(0L);
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");

        // Balances are a projection of ledger_entries, never written to
        // directly outside that projection - deleting a currency out from
        // under an existing balance would silently orphan the projection,
        // so the currency link is Restrict rather than Cascade.
        builder.HasOne(b => b.Currency)
            .WithMany()
            .HasForeignKey(b => b.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_balances_currencies_currency_id");

        builder.HasIndex(b => new { b.UserId, b.CurrencyId })
            .IsUnique()
            .HasDatabaseName("ix_balances_user_id_currency_id");

        builder.HasIndex(b => b.CurrencyId).HasDatabaseName("ix_balances_currency_id");
    }
}
