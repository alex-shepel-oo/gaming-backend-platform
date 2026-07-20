using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class ConversionRequestConfiguration : IEntityTypeConfiguration<ConversionRequest>
{
    public void Configure(EntityTypeBuilder<ConversionRequest> builder)
    {
        builder.ToTable("conversion_requests");
        builder.HasKey(r => r.Id).HasName("pk_conversion_requests");

        // Id doubles as the conversion's idempotency key - one row per key,
        // assigned by the caller rather than generated here.
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.FromCurrencyId).HasColumnName("from_currency_id").IsRequired();
        builder.Property(r => r.ToCurrencyId).HasColumnName("to_currency_id").IsRequired();
        builder.Property(r => r.GameId).HasColumnName("game_id");
        builder.Property(r => r.FromAmount).HasColumnName("from_amount").HasPrecision(20, 4);
        builder.Property(r => r.ToAmount).HasColumnName("to_amount").HasPrecision(20, 4);
        builder.Property(r => r.RateApplied).HasColumnName("rate_applied").HasPrecision(20, 8);
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(r => r.FailureReason).HasColumnName("failure_reason");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(r => r.FromCurrency)
            .WithMany()
            .HasForeignKey(r => r.FromCurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversion_requests_currencies_from_currency_id");

        builder.HasOne(r => r.ToCurrency)
            .WithMany()
            .HasForeignKey(r => r.ToCurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_conversion_requests_currencies_to_currency_id");

        // Backs both the runner's resumption scans and the owner-scoped GET.
        builder.HasIndex(r => new { r.UserId, r.Status })
            .HasDatabaseName("ix_conversion_requests_user_id_status");

        builder.HasIndex(r => r.FromCurrencyId).HasDatabaseName("ix_conversion_requests_from_currency_id");
        builder.HasIndex(r => r.ToCurrencyId).HasDatabaseName("ix_conversion_requests_to_currency_id");
    }
}
