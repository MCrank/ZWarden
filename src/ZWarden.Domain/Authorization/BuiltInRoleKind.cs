namespace ZWarden.Domain.Authorization;

/// <summary>
/// The built-in roles ZWarden seeds for every tenant (PRD 12A). <b>Platform Owner is deliberately
/// absent</b> — it is SaaS-only platform administration (v1.1), not a v1.0 self-hosted role. A tenant's
/// built-in roles are seeded from <see cref="BuiltInRoles"/>; a Tenant Owner or Administrator may also
/// author custom roles (which carry no kind).
/// </summary>
public enum BuiltInRoleKind
{
    /// <summary>Full control of one tenant — every permission (PRD 12A).</summary>
    TenantOwner,

    /// <summary>Broad operational administration; not tenant-level settings, host enrollment, or agent management.</summary>
    Administrator,

    /// <summary>Routine server lifecycle, updates, backups, and approved configuration operations.</summary>
    Operator,

    /// <summary>Limited player and server operations; the intentionally-limited default bundle (PRD 12A).</summary>
    Moderator,

    /// <summary>Read-only visibility.</summary>
    Viewer,

    /// <summary>Explicitly granted diagnostic access with no default mutating permissions.</summary>
    SupportDiagnostics,
}
