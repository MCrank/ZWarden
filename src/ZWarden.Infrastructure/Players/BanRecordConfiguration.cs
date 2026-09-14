using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Players;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// Maps the <c>BanRecords</c> table (F19; ADR 0027). A <see cref="BanRecord"/> is tenant-owned, so the model
/// conventions supply the typed-id key/column conversions and the tenant filter (ADR 0016) with no wiring here.
/// A <b>partial unique index</b> on <c>(TenantId, ServerId, Username)</c> filtered to <c>Status = 'Active'</c>
/// admits at most one active ban per user per Server, so a re-ban is caught rather than duplicated while lifted
/// history is retained. Its filter emits verbatim-identically on SQLite and PostgreSQL (the enum stores by name),
/// per ADR 0005, so both providers' migrations carry the same predicate with no branch.
/// </summary>
public sealed class BanRecordConfiguration : IEntityTypeConfiguration<BanRecord>
{
    /// <summary>The active-ban uniqueness index name (ADR 0005's verbatim DDL).</summary>
    internal const string ActiveBanIndexName = "UX_BanRecords_Active_PerServerUser";

    /// <summary>The active-ban filter predicate — accepted identically by both providers (ADR 0005); the enum
    /// stores by name.</summary>
    internal const string ActiveBanFilter = "\"Status\" = 'Active'";

    public void Configure(EntityTypeBuilder<BanRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("BanRecords");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ServerId).IsRequired();
        builder.Property(b => b.Username).IsRequired().HasMaxLength(BanRecord.MaxUsernameLength);
        builder.Property(b => b.Reason).HasMaxLength(BanRecord.MaxReasonLength);
        builder.Property(b => b.IssuedByUserId).IsRequired();
        builder.Property(b => b.IssuedAt)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(value, TimeSpan.Zero))
            .IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.LiftedByUserId);
        builder.Property(b => b.LiftedAt)
            .HasConversion(
                value => value == null ? (DateTime?)null : value.Value.UtcDateTime,
                value => value == null ? (DateTimeOffset?)null : new DateTimeOffset(value.Value, TimeSpan.Zero));

        // At most one active ban per (tenant, server, username); lifted rows fall outside the filter (ADR 0005).
        builder.HasIndex(nameof(BanRecord.TenantId), nameof(BanRecord.ServerId), nameof(BanRecord.Username))
            .IsUnique()
            .HasDatabaseName(ActiveBanIndexName)
            .HasFilter(ActiveBanFilter);

        // Backs the ban-list read (newest first, per Server).
        builder.HasIndex(nameof(BanRecord.TenantId), nameof(BanRecord.ServerId), nameof(BanRecord.IssuedAt));
    }
}
