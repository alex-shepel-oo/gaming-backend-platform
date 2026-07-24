using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        // platform.* rows only make sense platform-wide; game.* rows are allowed at any
        // game_id, including NULL - that's how a platform-wide role reaches every game
        // without a separate code path.
        builder.ToTable("role_permissions", table => table.HasCheckConstraint(
            "ck_role_permissions_platform_scope",
            "permission NOT LIKE 'platform.%' OR game_id IS NULL"));
        builder.HasKey(r => r.Id).HasName("pk_role_permissions");

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.Role).HasColumnName("role").HasConversion<short>();
        builder.Property(r => r.GameId).HasColumnName("game_id");
        builder.Property(r => r.Permission).HasColumnName("permission").IsRequired();
        builder.Property(r => r.GrantedAt).HasColumnName("granted_at");
        builder.Property(r => r.GrantedBy).HasColumnName("granted_by");

        builder.HasOne(r => r.Game)
            .WithMany()
            .HasForeignKey(r => r.GameId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_role_permissions_games_game_id");

        // Same NULL-distinctness problem as user_game_roles: a plain UNIQUE (role, game_id,
        // permission) would let Postgres accept duplicate platform-wide rows since NULL <> NULL.
        // Two partial indexes instead: one per-game grant, one platform-wide grant.
        builder.HasIndex(r => new { r.Role, r.GameId, r.Permission })
            .IsUnique()
            .HasFilter("game_id IS NOT NULL")
            .HasDatabaseName("ix_role_permissions_role_game_id_permission");

        builder.HasIndex(r => new { r.Role, r.Permission })
            .IsUnique()
            .HasFilter("game_id IS NULL")
            .HasDatabaseName("ix_role_permissions_role_permission_platform");

        builder.HasIndex(r => new { r.Role, r.GameId }).HasDatabaseName("ix_role_permissions_role_game_id");
        builder.HasIndex(r => r.GameId).HasDatabaseName("ix_role_permissions_game_id");
    }
}
