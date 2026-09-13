using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Servers;

namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// Maps the <c>Servers</c> table (F14) — the tenant-owned <see cref="Server"/> record (<c>srv-</c>). The
/// model conventions supply the typed-id conversions, the concurrency token, and the tenant filter
/// (ADR 0016). The last-reported <see cref="ServerRunState"/> is stored by name (additive, never a silent
/// renumber). The Server is indexed by its owning Agent because the inventory reconciler and per-host views
/// read by <c>AgentId</c>.
/// </summary>
public sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Servers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.AgentId).IsRequired();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.DockerContainerId).HasMaxLength(64);
        builder.Property(s => s.LastRunState).HasConversion<string>().HasMaxLength(32).IsRequired();
        // F16: the last-reported hierarchical health rollup, stored by name and nullable — null means "never
        // reported" (the age of LastHealthReportedAt is the staleness signal), not a default state.
        builder.Property(s => s.LastHealth).HasConversion<string>().HasMaxLength(32);
        // F17: the installed Steam build id observed after an update; untrusted Agent-observed text, length-bounded.
        builder.Property(s => s.InstalledBuildId).HasMaxLength(64);

        builder.HasIndex(s => s.AgentId);
    }
}
