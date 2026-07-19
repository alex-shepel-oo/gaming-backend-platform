using EconomyService.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EconomyService.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(m => m.MessageId).HasName("pk_processed_messages");

        builder.Property(m => m.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at").IsRequired();
    }
}
