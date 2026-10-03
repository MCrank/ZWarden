using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Mods;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Maps the <c>ServerWorkshopItems</c> table (#290, ADR 0047). A <see cref="ServerWorkshopItem"/> is tenant-owned, so
/// the model conventions supply the typed-id key/column conversions and the tenant filter (ADR 0016). The tags and
/// the two mod-id lists are primitive collections (a JSON column on SQLite, a <c>text[]</c> on PostgreSQL). One row
/// per (tenant, Server, Workshop id).
/// </summary>
public sealed class ServerWorkshopItemConfiguration : IEntityTypeConfiguration<ServerWorkshopItem>
{
    public void Configure(EntityTypeBuilder<ServerWorkshopItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ServerWorkshopItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ServerId).IsRequired();
        builder.Property(i => i.WorkshopId).IsRequired().HasMaxLength(ServerWorkshopItem.MaxWorkshopIdLength);
        builder.Property(i => i.Title).HasMaxLength(ServerWorkshopItem.MaxTitleLength);
        builder.Property(i => i.PreviewUrl).HasMaxLength(ServerWorkshopItem.MaxPreviewUrlLength);
        builder.PrimitiveCollection(i => i.Tags).IsRequired();
        builder.PrimitiveCollection(i => i.GuessedModIds).IsRequired();
        builder.PrimitiveCollection(i => i.ObservedModIds).IsRequired();
        builder.Property(i => i.OnDisk).IsRequired();
        builder.Property(i => i.SteamUpdatedAt).HasConversion(UtcConverters.Nullable);
        builder.Property(i => i.MetadataRefreshedAt).HasConversion(UtcConverters.Nullable);
        builder.Property(i => i.ObservedAt).HasConversion(UtcConverters.Nullable);
        builder.Property(i => i.InstalledUpdatedAt).HasConversion(UtcConverters.Nullable);
        builder.Ignore(i => i.HasMetadata);

        builder.HasIndex(nameof(ServerWorkshopItem.TenantId), nameof(ServerWorkshopItem.ServerId), nameof(ServerWorkshopItem.WorkshopId))
            .IsUnique();
    }
}
