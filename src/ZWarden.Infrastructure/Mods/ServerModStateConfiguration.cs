using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Mods;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Maps the <c>ServerModStates</c> table (#290, ADR 0047): one row per Server, keyed by its id, holding the configured
/// and booted-with mod lists as primitive collections (a JSON column on SQLite, a <c>text[]</c> on PostgreSQL), in
/// file order. Tenant-owned, so the tenant filter applies by convention (ADR 0016).
/// </summary>
public sealed class ServerModStateConfiguration : IEntityTypeConfiguration<ServerModState>
{
    public void Configure(EntityTypeBuilder<ServerModState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ServerModStates");
        builder.HasKey(s => s.ServerId);

        builder.PrimitiveCollection(s => s.ConfiguredWorkshopIds).IsRequired();
        builder.PrimitiveCollection(s => s.ConfiguredModIds).IsRequired();
        builder.PrimitiveCollection(s => s.BootedWorkshopIds).IsRequired();
        builder.PrimitiveCollection(s => s.BootedModIds).IsRequired();
        builder.Property(s => s.BootSnapshotPending).IsRequired();
        builder.Property(s => s.ConfigObservedAt).HasConversion(UtcConverters.Nullable);
        builder.Property(s => s.BootedAt).HasConversion(UtcConverters.Nullable);
        builder.Property(s => s.BootSnapshotAt).HasConversion(UtcConverters.Nullable);
        builder.Ignore(s => s.HasBootSnapshot);

        builder.HasIndex(nameof(ServerModState.TenantId));
    }
}
