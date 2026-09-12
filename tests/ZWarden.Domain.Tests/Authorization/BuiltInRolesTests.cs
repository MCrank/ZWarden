using ZWarden.Domain.Authorization;

namespace ZWarden.Domain.Tests.Authorization;

/// <summary>
/// F5 S2 (PR 1): the built-in role catalogue and, in particular, the <b>Moderator</b> default bundle,
/// turned into a red build (PRD 12A). The Moderator include/exclude lists are asserted both ways so a
/// seed regression that widens the role — handing Moderator <c>Console.Execute</c> or <c>Role.Manage</c>
/// — fails the build rather than shipping a privilege.
/// </summary>
public class BuiltInRolesTests
{
    [Test]
    public async Task Catalogue_holds_the_v1_built_in_roles_and_not_platform_owner()
    {
        // Platform Owner is SaaS-only (v1.1) and is deliberately not a v1.0 built-in kind (PRD 12A).
        BuiltInRoleKind[] expected =
        [
            BuiltInRoleKind.TenantOwner, BuiltInRoleKind.Administrator, BuiltInRoleKind.Operator,
            BuiltInRoleKind.Moderator, BuiltInRoleKind.Viewer, BuiltInRoleKind.SupportDiagnostics,
        ];

        List<BuiltInRoleKind> actual = BuiltInRoles.All.Select(r => r.Kind).ToList();

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(actual.Distinct().Count()).IsEqualTo(actual.Count);
        await Assert.That(Enum.GetNames<BuiltInRoleKind>()).DoesNotContain("PlatformOwner");
    }

    [Test]
    public async Task Every_built_in_bundle_references_only_catalogue_permissions_and_has_a_name()
    {
        foreach (BuiltInRoleDefinition role in BuiltInRoles.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(role.Name)).IsFalse();
            await Assert.That(role.Permissions.Count).IsGreaterThan(0);
            foreach (PermissionDefinition permission in role.Permissions)
            {
                await Assert.That(Permissions.Contains(permission.Name)).IsTrue();
            }
            // No duplicates within a bundle.
            await Assert.That(role.Permissions.Select(p => p.Name).Distinct().Count())
                .IsEqualTo(role.Permissions.Count);
        }
    }

    [Test]
    public async Task Moderator_includes_the_prd_bundle()
    {
        // PRD 12A "Moderator role" typical permissions, reconciled to catalogue names (ADR 0018):
        // Server.Health.View / Server.Log.View fold into Server.View; Mod.UpdateApproved -> Mod.Update.
        string[] included =
        [
            "Server.View", "Server.Restart",
            "Player.View", "Player.Kick", "Player.Ban", "Player.Unban",
            "Mod.View", "Mod.ApplyApprovedProfile", "Mod.Update",
        ];

        BuiltInRoleDefinition moderator = BuiltInRoles.Get(BuiltInRoleKind.Moderator);
        List<string> names = moderator.Permissions.Select(p => p.Name).ToList();

        foreach (string name in included)
        {
            await Assert.That(names).Contains(name);
        }

        names.Sort(StringComparer.Ordinal);
        string[] expected = [.. included];
        Array.Sort(expected, StringComparer.Ordinal);
        await Assert.That(names).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Moderator_excludes_the_high_risk_permissions()
    {
        // PRD 12A "shall not include" (the catalogue-present subset — Server.Delete / Mod.*Arbitrary are
        // not v1.0 catalogue permissions, so they are not grantable at all).
        string[] excluded =
        [
            "Server.Stop", "Server.Configuration.Edit", "Mod.Install", "Mod.Remove",
            "Backup.Restore", "Console.Execute", "Agent.Manage", "User.Manage",
            "Role.Manage", "Tenant.Manage",
        ];

        BuiltInRoleDefinition moderator = BuiltInRoles.Get(BuiltInRoleKind.Moderator);

        foreach (string name in excluded)
        {
            await Assert.That(moderator.Permissions.Any(p => p.Name == name)).IsFalse();
        }
    }

    [Test]
    public async Task Tenant_owner_holds_every_permission()
    {
        BuiltInRoleDefinition owner = BuiltInRoles.Get(BuiltInRoleKind.TenantOwner);

        await Assert.That(owner.Permissions.Count).IsEqualTo(Permissions.All.Count);
    }

    [Test]
    public async Task Viewer_is_read_only()
    {
        // "Read-only visibility" (PRD 12A): every Viewer permission is a .View, so no mutating capability.
        BuiltInRoleDefinition viewer = BuiltInRoles.Get(BuiltInRoleKind.Viewer);

        foreach (PermissionDefinition permission in viewer.Permissions)
        {
            await Assert.That(permission.Name.EndsWith(".View", StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task Support_diagnostics_has_diagnostic_access_and_no_mutating_permissions()
    {
        // "Explicitly granted diagnostic access with no default mutating permissions" (PRD 12A).
        BuiltInRoleDefinition support = BuiltInRoles.Get(BuiltInRoleKind.SupportDiagnostics);
        List<string> names = support.Permissions.Select(p => p.Name).ToList();

        await Assert.That(names).Contains("Diagnostics.View");
        await Assert.That(names).Contains("Diagnostics.Export");
        foreach (string mutating in new[]
                 {
                     "Server.Start", "Server.Stop", "Server.Restart", "Server.Configuration.Edit",
                     "Console.Execute", "Backup.Create", "Backup.Restore", "Backup.Delete",
                     "Player.Kick", "Player.Ban", "Mod.Install", "Mod.Remove",
                 })
        {
            await Assert.That(names).DoesNotContain(mutating);
        }
    }
}
