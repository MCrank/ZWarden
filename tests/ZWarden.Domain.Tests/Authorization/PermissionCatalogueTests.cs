using System.Text.RegularExpressions;
using ZWarden.Domain.Authorization;

namespace ZWarden.Domain.Tests.Authorization;

/// <summary>
/// F5 S1 (PR 1): the permission catalogue is the closed, single source of truth for permission names
/// (PRD 12A "Permission naming"). These turn the PRD list and its scope split into a red build — a
/// drifted, missing, or extra permission fails, which is the invisible-stale-set failure PRD 2.1 exists
/// to prevent.
/// </summary>
public class PermissionCatalogueTests
{
    // The canonical PRD 12A "Permission naming" list (docs/ZWarden_PRD_v1.2.md §12A). This is the
    // authoritative set of stable machine-readable names; the Moderator bundle's illustrative names are
    // reconciled onto these in S2 (ADR 0018).
    private static readonly string[] PrdPermissionNames =
    [
        "Server.View", "Server.Start", "Server.Stop", "Server.Restart",
        "Server.Configuration.View", "Server.Configuration.Edit",
        "Mod.View", "Mod.Install", "Mod.Remove", "Mod.Update", "Mod.ApplyApprovedProfile",
        "Player.View", "Player.Kick", "Player.Ban", "Player.Unban",
        "Console.View", "Console.Execute",
        "Backup.View", "Backup.Create", "Backup.Restore", "Backup.Delete",
        "Agent.View", "Agent.Manage",
        "Tenant.View", "Tenant.Manage", "Tenant.Members.Manage", "Tenant.Enrollment.Manage",
        "User.Manage", "Role.Manage", "Audit.View", "Diagnostics.View", "Diagnostics.Export",
    ];

    [Test]
    public async Task Catalogue_holds_exactly_the_prd_permission_names()
    {
        List<string> actual = Permissions.All.Select(p => p.Name).ToList();
        actual.Sort(StringComparer.Ordinal);
        string[] expected = [.. PrdPermissionNames];
        Array.Sort(expected, StringComparer.Ordinal);

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Names_are_unique_and_stable_machine_readable()
    {
        List<string> names = Permissions.All.Select(p => p.Name).ToList();

        await Assert.That(names.Distinct().Count()).IsEqualTo(names.Count);
        foreach (string name in names)
        {
            // Dotted PascalCase segments only - the wire/DB/policy currency, never free-form text.
            await Assert.That(Regex.IsMatch(name, "^[A-Za-z]+(\\.[A-Za-z]+)*$")).IsTrue();
        }
    }

    [Test]
    public async Task Server_facing_families_are_server_scopable_the_rest_tenant_wide()
    {
        // A permission's scope kind tells the decision layer whether a ServerId is meaningful (ADR 0018):
        // the per-server families are server-scopable; host/tenant-level permissions are tenant-wide.
        string[] serverScopablePrefixes = ["Server.", "Mod.", "Player.", "Console.", "Backup."];

        foreach (PermissionDefinition permission in Permissions.All)
        {
            bool expectedServerScopable =
                serverScopablePrefixes.Any(prefix => permission.Name.StartsWith(prefix, StringComparison.Ordinal));
            PermissionScope expected =
                expectedServerScopable ? PermissionScope.ServerScopable : PermissionScope.TenantWide;

            await Assert.That(permission.Scope).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Lookup_by_name_finds_catalogue_permissions_and_rejects_unknown()
    {
        await Assert.That(Permissions.TryGet("Server.Start", out PermissionDefinition found)).IsTrue();
        await Assert.That(found.Name).IsEqualTo("Server.Start");
        await Assert.That(Permissions.Contains("Role.Manage")).IsTrue();

        await Assert.That(Permissions.Contains("Server.Nonexistent")).IsFalse();
        await Assert.That(Permissions.TryGet("Server.Nonexistent", out _)).IsFalse();
    }
}
