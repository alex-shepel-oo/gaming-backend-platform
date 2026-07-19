using Microsoft.EntityFrameworkCore;

namespace Platform.Worker.Persistence;

// Cleanup-only view of identity_db: just enough of refresh_token_families
// and email_verification_codes to delete expired rows. Deliberately not the
// full IdentityDbContext model - the worker has no business reading the
// rest of that schema, only expiring it.
public sealed class IdentityCleanupDbContext(DbContextOptions<IdentityCleanupDbContext> options) : DbContext(options)
{
    public DbSet<RefreshTokenFamilyRecord> RefreshTokenFamilies => Set<RefreshTokenFamilyRecord>();
    public DbSet<EmailVerificationCodeRecord> EmailVerificationCodes => Set<EmailVerificationCodeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshTokenFamilyRecord>(builder =>
        {
            builder.ToTable("refresh_token_families");
            builder.HasKey(f => f.Id);
            builder.Property(f => f.Id).HasColumnName("id");
            builder.Property(f => f.ExpiresAt).HasColumnName("expires_at");
            builder.Property(f => f.RevokedAt).HasColumnName("revoked_at");
        });

        modelBuilder.Entity<EmailVerificationCodeRecord>(builder =>
        {
            builder.ToTable("email_verification_codes");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).HasColumnName("id");
            builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        });
    }
}

public sealed class RefreshTokenFamilyRecord
{
    public Guid Id { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
}

public sealed class EmailVerificationCodeRecord
{
    public Guid Id { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
