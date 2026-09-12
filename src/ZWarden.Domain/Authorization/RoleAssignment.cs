using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Authorization;

/// <summary>
/// The tenant-owned binding of a user to a <see cref="Role"/> — who holds which role, optionally narrowed
/// to a single Server (PRD 12A; ADR 0018). This is the <c>prm-</c> record the prefix registry reserves.
/// A null <see cref="ServerId"/> is a <b>tenant-wide</b> assignment (the role's permissions apply across
/// the tenant); a set <see cref="ServerId"/> narrows the role's server-scopable permissions to that one
/// Server. Being <see cref="ITenantOwned"/>, it is stamped and filtered by the ambient tenant (ADR 0016),
/// so a membership check is a filtered read and can never see another tenant's grants.
/// </summary>
public sealed class RoleAssignment : IVersioned, ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public RoleAssignment()
    {
    }

    /// <summary>The assignment identifier (<c>prm-&lt;uuid&gt;</c>).</summary>
    public PermissionAssignmentId Id { get; init; } = PermissionAssignmentId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The user who holds the role.</summary>
    public UserId UserId { get; init; }

    /// <summary>The role assigned.</summary>
    public RoleId RoleId { get; init; }

    /// <summary>The Server this assignment is narrowed to, or <c>null</c> for a tenant-wide assignment.</summary>
    public ServerId? ServerId { get; init; }

    /// <summary>True when the assignment is narrowed to a single Server.</summary>
    public bool IsServerScoped => ServerId is not null;

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>Creates a tenant-wide assignment of <paramref name="role"/> to <paramref name="user"/>.</summary>
    public static RoleAssignment TenantWide(TenantId tenant, UserId user, RoleId role) =>
        new() { Id = PermissionAssignmentId.New(), TenantId = tenant, UserId = user, RoleId = role, ServerId = null };

    /// <summary>Creates an assignment of <paramref name="role"/> to <paramref name="user"/> narrowed to
    /// <paramref name="server"/>.</summary>
    public static RoleAssignment ForServer(TenantId tenant, UserId user, RoleId role, ServerId server) =>
        new() { Id = PermissionAssignmentId.New(), TenantId = tenant, UserId = user, RoleId = role, ServerId = server };
}
