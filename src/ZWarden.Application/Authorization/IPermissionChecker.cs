using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Authorization;

/// <summary>
/// The one way business logic asks whether a principal is authorized (PRD 12; ADR 0018): does
/// <paramref name="user"/> hold <paramref name="permission"/> — optionally on a specific Server — in the
/// current tenant? An Application seam so business logic depends on no ASP.NET Core authorization types;
/// the ASP.NET Core policy surface (F5 enforcement slice) is built on top of this. <b>Fails closed</b> by
/// contract: no matching grant, no ambient tenant, or a server-scoped permission checked with no Server
/// all deny. Authorization is always decided here, server-side — hiding a UI control is never enough (PRD 12).
/// </summary>
public interface IPermissionChecker
{
    /// <summary>Evaluates whether <paramref name="user"/> holds <paramref name="permission"/> (optionally
    /// narrowed to <paramref name="server"/>) in the current tenant.</summary>
    Task<AuthorizationDecision> EvaluateAsync(
        UserId user,
        PermissionDefinition permission,
        ServerId? server = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The permission names <paramref name="user"/> holds <b>tenant-wide</b> in the current tenant (from
    /// their tenant-wide assignments). Fail-closed: an empty set with no ambient tenant. Backs the
    /// no-self-escalation guard — an actor may only author a role from permissions in this set.
    /// </summary>
    Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(
        UserId user,
        CancellationToken cancellationToken = default);
}
