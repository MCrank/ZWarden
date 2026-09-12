using System.Collections.Frozen;

namespace ZWarden.Domain.Authorization;

/// <summary>
/// The built-in role catalogue (PRD 12A) — the fixed bundles ZWarden seeds per tenant. The
/// <b>Moderator</b> bundle is the one PRD 12A pins exactly (asserted include-and-exclude by test); the
/// others follow the PRD's one-line descriptions and are recorded in ADR 0018 as a reviewed first cut.
/// Every bundle is drawn only from <see cref="Permissions"/>.
/// </summary>
/// <remarks>
/// Moderator reconciliation (ADR 0018): PRD 12A's illustrative <c>Server.Health.View</c> and
/// <c>Server.Log.View</c> fold into <c>Server.View</c>, and <c>Mod.UpdateApproved</c> maps to
/// <c>Mod.Update</c>; <c>Server.Delete</c> and <c>Mod.*Arbitrary</c> are not v1.0 catalogue permissions,
/// so they are not grantable at all.
/// </remarks>
public static class BuiltInRoles
{
    /// <summary>Full control of one tenant: every permission in the catalogue.</summary>
    public static readonly BuiltInRoleDefinition TenantOwner =
        new(BuiltInRoleKind.TenantOwner, "Tenant Owner", Permissions.All);

    /// <summary>Broad operational administration, minus tenant-level settings, host enrollment, and agent management.</summary>
    public static readonly BuiltInRoleDefinition Administrator =
        new(BuiltInRoleKind.Administrator, "Administrator",
            Permissions.All.Where(p =>
                p != Permissions.TenantManage
                && p != Permissions.TenantMembersManage
                && p != Permissions.TenantEnrollmentManage
                && p != Permissions.AgentManage).ToList());

    /// <summary>Routine server lifecycle, updates, backups, and approved configuration operations.</summary>
    public static readonly BuiltInRoleDefinition Operator =
        new(BuiltInRoleKind.Operator, "Operator",
        [
            Permissions.ServerView, Permissions.ServerStart, Permissions.ServerStop, Permissions.ServerRestart,
            Permissions.ServerConfigurationView, Permissions.ServerConfigurationEdit,
            Permissions.ModView, Permissions.ModUpdate, Permissions.ModApplyApprovedProfile,
            Permissions.PlayerView, Permissions.PlayerKick, Permissions.PlayerBan, Permissions.PlayerUnban,
            Permissions.ConsoleView,
            Permissions.BackupView, Permissions.BackupCreate, Permissions.BackupRestore,
            Permissions.AgentView,
        ]);

    /// <summary>The intentionally-limited default Moderator bundle, exactly per PRD 12A (reconciled to catalogue names).</summary>
    public static readonly BuiltInRoleDefinition Moderator =
        new(BuiltInRoleKind.Moderator, "Moderator",
        [
            Permissions.ServerView, Permissions.ServerRestart,
            Permissions.PlayerView, Permissions.PlayerKick, Permissions.PlayerBan, Permissions.PlayerUnban,
            Permissions.ModView, Permissions.ModApplyApprovedProfile, Permissions.ModUpdate,
        ]);

    /// <summary>Read-only visibility: every catalogue read permission, except the sensitive <c>Audit.View</c>.</summary>
    public static readonly BuiltInRoleDefinition Viewer =
        new(BuiltInRoleKind.Viewer, "Viewer",
            Permissions.All.Where(p =>
                p.Name.EndsWith(".View", StringComparison.Ordinal) && p != Permissions.AuditView).ToList());

    /// <summary>Diagnostic read/export access with no mutating permissions (time-limiting is a safety-rule concern).</summary>
    public static readonly BuiltInRoleDefinition SupportDiagnostics =
        new(BuiltInRoleKind.SupportDiagnostics, "Support/Diagnostics",
        [
            Permissions.DiagnosticsView, Permissions.DiagnosticsExport, Permissions.AuditView,
            Permissions.ServerView, Permissions.ServerConfigurationView,
            Permissions.ConsoleView, Permissions.BackupView, Permissions.AgentView,
        ]);

    /// <summary>Every built-in role definition — the closed set ZWarden seeds per tenant.</summary>
    public static IReadOnlyList<BuiltInRoleDefinition> All { get; } =
        [TenantOwner, Administrator, Operator, Moderator, Viewer, SupportDiagnostics];

    private static readonly FrozenDictionary<BuiltInRoleKind, BuiltInRoleDefinition> ByKind =
        All.ToFrozenDictionary(r => r.Kind);

    /// <summary>The definition for a built-in role kind.</summary>
    public static BuiltInRoleDefinition Get(BuiltInRoleKind kind) => ByKind[kind];
}
