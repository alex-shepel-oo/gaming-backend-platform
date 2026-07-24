using EconomyService.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class ProjectedEventCountConfiguration : IEntityTypeConfiguration<ProjectedEventCount>
{
    public void Configure(EntityTypeBuilder<ProjectedEventCount> builder)
    {
        builder.ToTable("projected_event_counts");
        builder.HasKey(c => c.EventType).HasName("pk_projected_event_counts");

        builder.Property(c => c.EventType).HasColumnName("event_type").ValueGeneratedNever();
        builder.Property(c => c.Count).HasColumnName("count").IsRequired();
    }
}
