using EconomyService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies", table => table.HasCheckConstraint(
            "ck_currencies_scope_game_id",
            "(scope = 0 AND game_id IS NULL) OR (scope = 1 AND game_id IS NOT NULL)"));
        builder.HasKey(c => c.Id).HasName("pk_currencies");

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.Code).HasColumnName("code").IsRequired();
        builder.Property(c => c.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(c => c.Scope).HasColumnName("scope").HasConversion<short>();
        builder.Property(c => c.GameId).HasColumnName("game_id");
        builder.Property(c => c.Decimals).HasColumnName("decimals").HasDefaultValue((short)2).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");

        // Postgres treats NULLs in a unique index as distinct from each other,
        // so a plain UNIQUE (code, game_id) would accept duplicate platform-wide
        // codes. Two partial indexes instead: one per-game code, one platform code.
        builder.HasIndex(c => new { c.Code, c.GameId })
            .IsUnique()
            .HasFilter("game_id IS NOT NULL")
            .HasDatabaseName("ix_currencies_code_game_id");

        builder.HasIndex(c => c.Code)
            .IsUnique()
            .HasFilter("game_id IS NULL")
            .HasDatabaseName("ix_currencies_code_platform");

        builder.HasIndex(c => c.GameId).HasDatabaseName("ix_currencies_game_id");
    }
}
