using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Authorization;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Maps the <c>RolePermissionGrants</c> table (F5) — the role→permission bundle. Keyed by
/// (<c>RoleId</c>, <c>PermissionName</c>), so a role grants a permission at most once. It is a dependent
/// of <see cref="Role"/> (relationship configured on <see cref="RoleConfiguration"/>) and is reached only
/// through its role, which carries the tenant filter, so it needs no tenant scope of its own.
/// </summary>
public sealed class RolePermissionGrantConfiguration : IEntityTypeConfiguration<RolePermissionGrant>
{
    public void Configure(EntityTypeBuilder<RolePermissionGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RolePermissionGrants");
        builder.HasKey(nameof(RolePermissionGrant.RoleId), nameof(RolePermissionGrant.PermissionName));
        builder.Property(g => g.PermissionName).IsRequired().HasMaxLength(128);
    }
}
