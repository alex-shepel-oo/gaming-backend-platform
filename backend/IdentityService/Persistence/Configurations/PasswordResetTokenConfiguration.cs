using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(t => t.Id).HasName("pk_password_reset_tokens");

        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        builder.Property(t => t.ConsumedAt).HasColumnName("consumed_at");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_password_reset_tokens_users_user_id");

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_password_reset_tokens_token_hash");

        // Invariant "one live token per account" lives in the database, not the
        // service layer: a check-then-act in code would race across replicas.
        builder.HasIndex(t => t.UserId)
            .IsUnique()
            .HasFilter("consumed_at IS NULL")
            .HasDatabaseName("ix_password_reset_tokens_user_id_active");

        // Backs the per-account request cooldown, which counts requests over a
        // window including already-consumed tokens, so it cannot be partial.
        builder.HasIndex(t => new { t.UserId, t.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_password_reset_tokens_user_id_created_at");

        builder.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("ix_password_reset_tokens_expires_at");
    }
}
