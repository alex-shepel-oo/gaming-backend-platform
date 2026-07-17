using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class RevokedAccessTokenConfiguration : IEntityTypeConfiguration<RevokedAccessToken>
{
    public void Configure(EntityTypeBuilder<RevokedAccessToken> builder)
    {
        builder.ToTable("revoked_access_tokens");
        builder.HasKey(t => t.Jti).HasName("pk_revoked_access_tokens");

        builder.Property(t => t.Jti).HasColumnName("jti").ValueGeneratedNever();
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.GameId).HasColumnName("game_id");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.Reason).HasColumnName("reason").HasConversion<short>();

        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_revoked_access_tokens_expires_at");
    }
}
