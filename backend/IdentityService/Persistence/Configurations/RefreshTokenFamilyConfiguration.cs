using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class RefreshTokenFamilyConfiguration : IEntityTypeConfiguration<RefreshTokenFamily>
{
    public void Configure(EntityTypeBuilder<RefreshTokenFamily> builder)
    {
        builder.ToTable("refresh_token_families");
        builder.HasKey(f => f.Id).HasName("pk_refresh_token_families");

        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(f => f.GameId).HasColumnName("game_id");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.ExpiresAt).HasColumnName("expires_at");
        builder.Property(f => f.RevokedAt).HasColumnName("revoked_at");
        builder.Property(f => f.RevokedReason).HasColumnName("revoked_reason").HasConversion<short?>();
        builder.Property(f => f.CreatedByIp).HasColumnName("created_by_ip");
        builder.Property(f => f.UserAgent).HasColumnName("user_agent");

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_refresh_token_families_users_user_id");

        builder.HasOne(f => f.Game)
            .WithMany()
            .HasForeignKey(f => f.GameId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_refresh_token_families_games_game_id");

        builder.HasIndex(f => f.GameId)
            .HasDatabaseName("ix_refresh_token_families_game_id");

        builder.HasIndex(f => new { f.UserId, f.GameId })
            .HasDatabaseName("ix_refresh_token_families_user_id_game_id");

        builder.HasIndex(f => f.ExpiresAt)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_token_families_expires_at");
    }
}
