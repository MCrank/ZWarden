using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Authorization;

/// <summary>
/// The outcome of an authorization check (PRD 12A; ADR 0018): allow or deny, with a human-readable
/// <see cref="Reason"/> for diagnostics and audit. The reason names the permission (and Server, when
/// server-scoped) or why the check failed closed — it never echoes a request-supplied hint or a secret.
/// </summary>
public sealed record AuthorizationDecision
{
    private AuthorizationDecision(bool isAllowed, string reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    /// <summary>Whether the action is permitted.</summary>
    public bool IsAllowed { get; }

    /// <summary>Why — the granted permission, or the fail-closed reason.</summary>
    public string Reason { get; }

    /// <summary>An allow decision for a permission, optionally on a specific Server.</summary>
    public static AuthorizationDecision Allow(PermissionDefinition permission, ServerId? server)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return new AuthorizationDecision(
            true,
            server is null ? $"Granted {permission.Name}." : $"Granted {permission.Name} on {server}.");
    }

    /// <summary>A deny decision carrying the reason it failed closed.</summary>
    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}
