using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Authorization;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Maps the <c>Roles</c> table (F5). A <see cref="Role"/> is tenant-owned, so the model conventions add
/// the typed-id key conversion, the concurrency token, and the tenant filter (ADR 0016) with no wiring
/// here. Role names are unique <b>per tenant</b> (ADR 0018) — two tenants may both hold an "Ops" role,
/// one tenant may not — and the permission bundle is a cascade-owned child collection reached only
/// through the role.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.Property(r => r.BuiltIn).HasConversion<string>().HasMaxLength(64);

        // Unique per tenant, not globally - the whole point of F5 owning roles (ADR 0018).
        builder.HasIndex(nameof(Role.TenantId), nameof(Role.Name)).IsUnique();

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(g => g.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
