using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class ConversionRateConfiguration : IEntityTypeConfiguration<ConversionRate>
{
    public void Configure(EntityTypeBuilder<ConversionRate> builder)
    {
        builder.ToTable("conversion_rates");
        builder.HasKey(r => r.Id).HasName("pk_conversion_rates");

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.FromCurrencyId).HasColumnName("from_currency_id").IsRequired();
        builder.Property(r => r.ToCurrencyId).HasColumnName("to_currency_id").IsRequired();
        builder.Property(r => r.Rate).HasColumnName("rate").HasPrecision(18, 6);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasOne(r => r.FromCurrency)
            .WithMany()
            .HasForeignKey(r => r.FromCurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversion_rates_currencies_from_currency_id");

        builder.HasOne(r => r.ToCurrency)
            .WithMany()
            .HasForeignKey(r => r.ToCurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversion_rates_currencies_to_currency_id");

        builder.HasIndex(r => new { r.FromCurrencyId, r.ToCurrencyId })
            .IsUnique()
            .HasDatabaseName("ix_conversion_rates_from_currency_id_to_currency_id");

        builder.HasIndex(r => r.ToCurrencyId).HasDatabaseName("ix_conversion_rates_to_currency_id");
    }
}
