using System.Collections.Frozen;

namespace ZWarden.Domain.Authorization;

/// <summary>
/// The closed permission catalogue — the single source of truth for the permission names PRD 12A fixes
/// ("Permission naming"). Nothing outside this class defines a permission; roles bundle these, assignments
/// grant those roles, and the decision layer resolves against them (ADR 0018). A test asserts the set
/// equals the PRD list exactly, so adding, renaming, or dropping a permission is a deliberate
/// catalogue-and-test change (and, for a new capability area, an ADR) rather than an ad-hoc string.
/// </summary>
/// <remarks>
/// Scope kinds (ADR 0018): the per-server families — <c>Server.*</c>, <c>Mod.*</c>, <c>Player.*</c>,
/// <c>Console.*</c>, <c>Backup.*</c> — are <see cref="PermissionScope.ServerScopable"/>; host- and
/// tenant-level permissions (<c>Agent.*</c>, <c>Tenant.*</c>, <c>User.Manage</c>, <c>Role.Manage</c>,
/// <c>Audit.View</c>, <c>Diagnostics.*</c>) are <see cref="PermissionScope.TenantWide"/>.
/// </remarks>
public static class Permissions
{
    private static PermissionDefinition Server(string name) => new(name, PermissionScope.ServerScopable);
    private static PermissionDefinition Tenant(string name) => new(name, PermissionScope.TenantWide);

    // Server lifecycle and configuration (per-server).
    public static readonly PermissionDefinition ServerView = Server("Server.View");
    public static readonly PermissionDefinition ServerStart = Server("Server.Start");
    public static readonly PermissionDefinition ServerStop = Server("Server.Stop");
    public static readonly PermissionDefinition ServerRestart = Server("Server.Restart");
    public static readonly PermissionDefinition ServerConfigurationView = Server("Server.Configuration.View");
    public static readonly PermissionDefinition ServerConfigurationEdit = Server("Server.Configuration.Edit");

    // Mods (per-server).
    public static readonly PermissionDefinition ModView = Server("Mod.View");
    public static readonly PermissionDefinition ModInstall = Server("Mod.Install");
    public static readonly PermissionDefinition ModRemove = Server("Mod.Remove");
    public static readonly PermissionDefinition ModUpdate = Server("Mod.Update");
    public static readonly PermissionDefinition ModApplyApprovedProfile = Server("Mod.ApplyApprovedProfile");

    // Players (per-server).
    public static readonly PermissionDefinition PlayerView = Server("Player.View");
    public static readonly PermissionDefinition PlayerKick = Server("Player.Kick");
    public static readonly PermissionDefinition PlayerBan = Server("Player.Ban");
    public static readonly PermissionDefinition PlayerUnban = Server("Player.Unban");

    // Console (per-server).
    public static readonly PermissionDefinition ConsoleView = Server("Console.View");
    public static readonly PermissionDefinition ConsoleExecute = Server("Console.Execute");

    // Backups (per-server).
    public static readonly PermissionDefinition BackupView = Server("Backup.View");
    public static readonly PermissionDefinition BackupCreate = Server("Backup.Create");
    public static readonly PermissionDefinition BackupRestore = Server("Backup.Restore");
    public static readonly PermissionDefinition BackupDelete = Server("Backup.Delete");

    // Agents / hosts (tenant-wide).
    public static readonly PermissionDefinition AgentView = Tenant("Agent.View");
    public static readonly PermissionDefinition AgentManage = Tenant("Agent.Manage");

    // Tenant administration (tenant-wide).
    public static readonly PermissionDefinition TenantView = Tenant("Tenant.View");
    public static readonly PermissionDefinition TenantManage = Tenant("Tenant.Manage");
    public static readonly PermissionDefinition TenantMembersManage = Tenant("Tenant.Members.Manage");
    public static readonly PermissionDefinition TenantEnrollmentManage = Tenant("Tenant.Enrollment.Manage");

    // Users, roles, audit, diagnostics (tenant-wide).
    public static readonly PermissionDefinition UserManage = Tenant("User.Manage");
    public static readonly PermissionDefinition RoleManage = Tenant("Role.Manage");
    public static readonly PermissionDefinition AuditView = Tenant("Audit.View");
    public static readonly PermissionDefinition DiagnosticsView = Tenant("Diagnostics.View");
    public static readonly PermissionDefinition DiagnosticsExport = Tenant("Diagnostics.Export");

    /// <summary>Every permission in the catalogue, in declaration order — the closed set.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        ServerView, ServerStart, ServerStop, ServerRestart,
        ServerConfigurationView, ServerConfigurationEdit,
        ModView, ModInstall, ModRemove, ModUpdate, ModApplyApprovedProfile,
        PlayerView, PlayerKick, PlayerBan, PlayerUnban,
        ConsoleView, ConsoleExecute,
        BackupView, BackupCreate, BackupRestore, BackupDelete,
        AgentView, AgentManage,
        TenantView, TenantManage, TenantMembersManage, TenantEnrollmentManage,
        UserManage, RoleManage, AuditView, DiagnosticsView, DiagnosticsExport,
    ];

    private static readonly FrozenDictionary<string, PermissionDefinition> ByName =
        All.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);

    /// <summary>Looks up a permission by its stable name; false for any name outside the catalogue.</summary>
    public static bool TryGet(string name, out PermissionDefinition permission) => ByName.TryGetValue(name, out permission!);

    /// <summary>True when <paramref name="name"/> is a catalogue permission name.</summary>
    public static bool Contains(string name) => ByName.ContainsKey(name);
}
