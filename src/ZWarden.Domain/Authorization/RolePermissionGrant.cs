using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Authorization;

/// <summary>
/// One permission granted to a <see cref="Role"/> — a row in the role→permission bundle (ADR 0018). The
/// <see cref="PermissionName"/> is a stable catalogue name (see <see cref="Permissions"/>), never a
/// free-form string. A dependent of <see cref="Role"/>: it is always reached through its role (which is
/// tenant-owned and filtered), so it carries no tenant scope of its own.
/// </summary>
/// <remarks>Named with a <c>Grant</c> suffix rather than ending in <c>Permission</c> (a reserved suffix,
/// CA1711), and to distinguish the role→permission grant from the user→role <c>RoleAssignment</c>.</remarks>
public sealed class RolePermissionGrant
{
    /// <summary>EF materialization.</summary>
    private RolePermissionGrant()
    {
    }

    /// <summary>Grants <paramref name="permissionName"/> to the role identified by <paramref name="roleId"/>.</summary>
    public RolePermissionGrant(RoleId roleId, string permissionName)
    {
        RoleId = roleId;
        PermissionName = permissionName;
    }

    /// <summary>The owning role.</summary>
    public RoleId RoleId { get; init; }

    /// <summary>The catalogue permission name granted (e.g. <c>Server.Start</c>).</summary>
    public string PermissionName { get; init; } = string.Empty;
}
