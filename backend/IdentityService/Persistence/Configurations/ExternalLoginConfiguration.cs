using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_logins");
        builder.HasKey(l => l.Id).HasName("pk_external_logins");

        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(l => l.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(l => l.Provider).HasColumnName("provider").IsRequired();
        builder.Property(l => l.ProviderUserId).HasColumnName("provider_user_id").IsRequired();
        builder.Property(l => l.LinkedAt).HasColumnName("linked_at");

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_external_logins_users_user_id");

        builder.HasIndex(l => new { l.Provider, l.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("ix_external_logins_provider_provider_user_id");

        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("ix_external_logins_user_id");
    }
}
