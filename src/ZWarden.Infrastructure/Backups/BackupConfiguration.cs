using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Backups;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// Maps the <c>Backups</c> table (F24; ADR 0028). A <see cref="Backup"/> is tenant-owned, so the model
/// conventions supply the typed-id key/column conversions and the tenant filter (ADR 0016) with no wiring here.
/// Write-once metadata about an Agent-resident archive: the relative locator, the produced size, the lowercase-hex
/// SHA-256 (the integrity value F25 re-verifies), and the retention metadata. A composite index on
/// <c>(TenantId, ServerId, CreatedAt)</c> backs the per-Server, newest-first backup list read.
/// </summary>
public sealed class BackupConfiguration : IEntityTypeConfiguration<Backup>
{
    public void Configure(EntityTypeBuilder<Backup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Backups");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ServerId).IsRequired();
        builder.Property(b => b.AgentId).IsRequired();
        builder.Property(b => b.ArchiveName).IsRequired().HasMaxLength(Backup.MaxArchiveNameLength);
        builder.Property(b => b.SizeBytes).IsRequired();
        builder.Property(b => b.Sha256).IsRequired().HasMaxLength(Backup.MaxChecksumLength);
        builder.Property(b => b.Reason).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.CreatedAt)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(value, TimeSpan.Zero))
            .IsRequired();
        builder.Property(b => b.ExpiresAt)
            .HasConversion(
                value => value == null ? (DateTime?)null : value.Value.UtcDateTime,
                value => value == null ? (DateTimeOffset?)null : new DateTimeOffset(value.Value, TimeSpan.Zero));

        // Backs the per-Server backup list read (newest first).
        builder.HasIndex(nameof(Backup.TenantId), nameof(Backup.ServerId), nameof(Backup.CreatedAt));
    }
}
