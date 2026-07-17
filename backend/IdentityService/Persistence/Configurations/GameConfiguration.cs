using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Persistence.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("games");
        builder.HasKey(g => g.Id).HasName("pk_games");

        builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(g => g.Slug).HasColumnName("slug").IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").IsRequired();
        builder.Property(g => g.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(g => g.Slug).IsUnique().HasDatabaseName("ix_games_slug");
    }
}
