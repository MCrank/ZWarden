using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Settings;

namespace ZWarden.Infrastructure.Settings;

/// <summary>
/// Maps the <c>ControlPlaneSettings</c> table (#345, ADR 0048). The aggregate is tenant-owned and versioned, so the
/// model conventions supply the typed-id conversion, the concurrency token and the tenant filter (ADR 0016). Each
/// setting is a nullable column (null = config applies); a <b>unique index on <c>TenantId</c></b> enforces one row
/// per tenant at the database.
/// </summary>
public sealed class ControlPlaneSettingsConfiguration : IEntityTypeConfiguration<ControlPlaneSettings>
{
    public void Configure(EntityTypeBuilder<ControlPlaneSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ControlPlaneSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.InstanceName).HasMaxLength(ControlPlaneSettings.MaxInstanceNameLength);

        builder.HasIndex(nameof(ControlPlaneSettings.TenantId)).IsUnique();
    }
}
