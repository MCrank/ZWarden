using Microsoft.AspNetCore.Authorization;
using ZWarden.Domain.Authorization;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// An ASP.NET Core authorization requirement that a principal hold a specific catalogue
/// <see cref="PermissionDefinition"/> (ADR 0018). Produced by <see cref="PermissionPolicyProvider"/> for
/// any policy named after a catalogue permission, and satisfied by <see cref="PermissionAuthorizationHandler"/>
/// (tenant-wide) or the server-scoped resource handler — both backed by the fail-closed
/// <see cref="ZWarden.Application.Authorization.IPermissionChecker"/>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(PermissionDefinition permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        Permission = permission;
    }

    /// <summary>The permission the principal must hold.</summary>
    public PermissionDefinition Permission { get; }
}
