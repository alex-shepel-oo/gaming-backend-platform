using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class UserGameRoleConfiguration : IEntityTypeConfiguration<UserGameRole>
{
    public void Configure(EntityTypeBuilder<UserGameRole> builder)
    {
        builder.ToTable("user_game_roles");
        builder.HasKey(r => r.Id).HasName("pk_user_game_roles");

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.GameId).HasColumnName("game_id");
        builder.Property(r => r.Role).HasColumnName("role").HasConversion<short>();
        builder.Property(r => r.GrantedAt).HasColumnName("granted_at");

        builder.HasOne(r => r.User)
            .WithMany(u => u.UserGameRoles)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_game_roles_users_user_id");

        builder.HasOne(r => r.Game)
            .WithMany(g => g.UserGameRoles)
            .HasForeignKey(r => r.GameId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_game_roles_games_game_id");

        // Postgres treats NULLs in a unique index as distinct from each
        // other, so a plain UNIQUE (user_id, game_id) would accept
        // duplicate platform-wide roles. Two partial indexes instead:
        // one per-tenant role per (user, game), one platform role per user.
        builder.HasIndex(r => new { r.UserId, r.GameId })
            .IsUnique()
            .HasFilter("game_id IS NOT NULL")
            .HasDatabaseName("ix_user_game_roles_user_id_game_id");

        builder.HasIndex(r => r.UserId)
            .IsUnique()
            .HasFilter("game_id IS NULL")
            .HasDatabaseName("ix_user_game_roles_user_id_platform_role");

        builder.HasIndex(r => r.GameId).HasDatabaseName("ix_user_game_roles_game_id");
    }
}
