using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class EmailVerificationCodeConfiguration : IEntityTypeConfiguration<EmailVerificationCode>
{
    public void Configure(EntityTypeBuilder<EmailVerificationCode> builder)
    {
        builder.ToTable("email_verification_codes");
        builder.HasKey(c => c.Id).HasName("pk_email_verification_codes");

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(c => c.GameId).HasColumnName("game_id");
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(c => c.SentToEmail).HasColumnName("sent_to_email").IsRequired();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_email_verification_codes_users_user_id");

        builder.HasOne(c => c.Game)
            .WithMany()
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_email_verification_codes_games_game_id");

        builder.HasIndex(c => c.GameId)
            .HasDatabaseName("ix_email_verification_codes_game_id");

        // Invariant "one live code per account" lives in the database, not the
        // service layer -- a check-then-act in code would race across replicas.
        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasFilter("consumed_at IS NULL")
            .HasDatabaseName("ix_email_verification_codes_user_id_active");

        // Backs the resend cooldown query, which counts sends over the last
        // hour including already-consumed codes, so it cannot be partial.
        builder.HasIndex(c => new { c.UserId, c.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_email_verification_codes_user_id_created_at");

        builder.HasIndex(c => c.ExpiresAt)
            .HasDatabaseName("ix_email_verification_codes_expires_at");
    }
}
