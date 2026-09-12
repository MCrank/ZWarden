using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Authorization;

/// <summary>
/// A role — a manageable bundle of permissions (PRD 12A). F5 owns roles (ADR 0018): a <see cref="Role"/>
/// is <b>tenant-owned</b>, so the tenant filter isolates each tenant's roles (ADR 0016) and two tenants
/// may hold same-named custom roles without collision. Built-in roles carry a <see cref="BuiltIn"/> kind
/// and are seeded per tenant from <see cref="BuiltInRoles"/>; custom roles carry none. It is distinct
/// from F4's Identity <c>ApplicationRole</c>, which stays a coarse identity-claim role.
/// </summary>
public sealed class Role : IVersioned, ITenantOwned
{
    private readonly List<RolePermissionGrant> _permissions = [];

    /// <summary>Creates an empty role with a fresh <see cref="RoleId"/> (used by factories and EF).</summary>
    public Role()
    {
    }

    /// <summary>The role identifier (<c>rol-&lt;uuid&gt;</c>).</summary>
    public RoleId Id { get; init; } = RoleId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The human-readable role name (unique per tenant).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The built-in kind this role was seeded from, or <c>null</c> for a custom role.</summary>
    public BuiltInRoleKind? BuiltIn { get; init; }

    /// <summary>True when this is a seeded built-in role rather than a tenant-authored custom one.</summary>
    public bool IsBuiltIn => BuiltIn is not null;

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>The permissions this role grants.</summary>
    public IReadOnlyCollection<RolePermissionGrant> Permissions => _permissions;

    /// <summary>True when this role grants the named catalogue permission.</summary>
    public bool HasPermission(string permissionName) =>
        _permissions.Exists(p => string.Equals(p.PermissionName, permissionName, StringComparison.Ordinal));

    /// <summary>Grants a catalogue permission to this role; idempotent (granting twice is a no-op).</summary>
    public void Grant(PermissionDefinition permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        if (!HasPermission(permission.Name))
        {
            _permissions.Add(new RolePermissionGrant(Id, permission.Name));
        }
    }

    /// <summary>Revokes a permission from this role; a no-op when it was not granted.</summary>
    public void Revoke(PermissionDefinition permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        _permissions.RemoveAll(p => string.Equals(p.PermissionName, permission.Name, StringComparison.Ordinal));
    }

    /// <summary>Creates a tenant-owned custom role (no built-in kind).</summary>
    public static Role CreateCustom(TenantId tenant, string name) =>
        new() { Id = RoleId.New(), TenantId = tenant, Name = name, BuiltIn = null };

    /// <summary>Materializes a tenant-owned built-in role from its definition, granting its whole bundle.</summary>
    public static Role FromBuiltIn(TenantId tenant, BuiltInRoleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Role role = new() { Id = RoleId.New(), TenantId = tenant, Name = definition.Name, BuiltIn = definition.Kind };
        foreach (PermissionDefinition permission in definition.Permissions)
        {
            role.Grant(permission);
        }

        return role;
    }
}
