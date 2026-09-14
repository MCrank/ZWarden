using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Configuration;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// Maps the <c>ConfigurationRevisions</c> table (F20b; ADR 0011). A <see cref="ConfigurationRevision"/> is
/// tenant-owned and versioned, so the model conventions supply the typed-id conversions, the concurrency
/// token, and the tenant filter (ADR 0016) with no wiring here. The <see cref="PzConfigFile"/> is stored by
/// name (additive, never a silent renumber). The <see cref="ConfigurationRevision.CanonicalSnapshot"/> is the
/// order-normalized parsed values — potentially the whole file's scalars — so it is left unbounded (TEXT);
/// the <see cref="ConfigurationRevision.SnapshotHash"/> is a fixed 64-char SHA-256 hex. A composite index on
/// <c>(TenantId, ServerId, File, CreatedAt)</c> backs both the "latest revision per file" drift-baseline read
/// and the newest-first history read.
/// </summary>
public sealed class ConfigurationRevisionConfiguration : IEntityTypeConfiguration<ConfigurationRevision>
{
    public void Configure(EntityTypeBuilder<ConfigurationRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ConfigurationRevisions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ServerId).IsRequired();
        builder.Property(r => r.File).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.CanonicalSnapshot).IsRequired();
        builder.Property(r => r.SnapshotHash).IsRequired().HasMaxLength(ConfigurationRevision.HashLength);
        builder.Property(r => r.CreatedAt)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(value, TimeSpan.Zero))
            .IsRequired();
        builder.Property(r => r.CreatedByUserId);

        // Backs the latest-per-file drift baseline lookup and the newest-first revision history.
        builder.HasIndex(
            nameof(ConfigurationRevision.TenantId),
            nameof(ConfigurationRevision.ServerId),
            nameof(ConfigurationRevision.File),
            nameof(ConfigurationRevision.CreatedAt));
    }
}
