using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Audit;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// Maps the <c>AuditEvents</c> table (F6; ADR 0019). An <see cref="AuditEvent"/> is tenant-owned, so the
/// model conventions add the typed-id key/column conversions and the tenant filter (ADR 0016) with no
/// wiring here; it is append-only, so there is no concurrency token to configure. The nullable typed-id
/// properties (<see cref="AuditEvent.ActorUserId"/>, <see cref="AuditEvent.ServerId"/>) are declared so the
/// converter convention discovers them. The <c>(TenantId, OccurredAt)</c> and <c>(TenantId, CorrelationId)</c>
/// indexes back the viewer's newest-first and correlation reads.
/// </summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("AuditEvents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.Action).IsRequired().HasMaxLength(256);
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.ActorUserId);
        builder.Property(a => a.ServerId);
        builder.Property(a => a.CorrelationId).HasMaxLength(128);
        builder.Property(a => a.Detail).HasMaxLength(1024);

        builder.HasIndex(nameof(AuditEvent.TenantId), nameof(AuditEvent.OccurredAt));
        builder.HasIndex(nameof(AuditEvent.TenantId), nameof(AuditEvent.CorrelationId));
    }
}
