using ZWarden.Domain.Authorization;
using ZWarden.Domain.Settings;

namespace ZWarden.Domain.Tests.Settings;

/// <summary>
/// #345: the tenant's <see cref="ControlPlaneSettings"/> (<c>cps-</c>) store. Every value is optional (null = the
/// config value or the default applies). The instance name is trimmed, 1-64 characters, free of control characters,
/// and a blank name clears the override.
/// </summary>
public sealed class ControlPlaneSettingsTests
{
    [Test]
    public async Task Create_starts_with_no_overrides()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        await Assert.That(settings.InstanceName).IsNull();
        await Assert.That(settings.TenantId.IsEmpty).IsTrue(); // stamped by the interceptor on insert
        await Assert.That(settings.Id.ToString()).StartsWith("cps-");
    }

    [Test]
    public async Task The_instance_name_is_trimmed()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        settings.SetInstanceName("  Knox Ops  ");

        await Assert.That(settings.InstanceName).IsEqualTo("Knox Ops");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task A_blank_instance_name_clears_the_override(string? name)
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();
        settings.SetInstanceName("Knox Ops");

        settings.SetInstanceName(name);

        await Assert.That(settings.InstanceName).IsNull();
    }

    [Test]
    public async Task An_instance_name_of_64_characters_is_accepted_and_65_is_not()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        settings.SetInstanceName(new string('a', 64));

        await Assert.That(settings.InstanceName!.Length).IsEqualTo(64);
        await Assert.That(() => settings.SetInstanceName(new string('a', 65))).Throws<ArgumentException>();
        await Assert.That(settings.InstanceName!.Length).IsEqualTo(64); // unchanged by the rejected value
    }

    [Test]
    [Arguments("Knox\nOps")]
    [Arguments("Knox\u0007Ops")]
    [Arguments("Knox​Ops")] // a zero-width space: format characters are refused like control characters
    public async Task An_instance_name_with_control_characters_is_refused(string name)
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        await Assert.That(() => settings.SetInstanceName(name)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Owner_and_administrator_may_manage_settings_but_no_lesser_role()
    {
        PermissionDefinition manage = Permissions.TenantSettingsManage;

        await Assert.That(manage.Name).IsEqualTo("Tenant.Settings.Manage");
        await Assert.That(BuiltInRoles.TenantOwner.Permissions).Contains(manage);
        await Assert.That(BuiltInRoles.Administrator.Permissions).Contains(manage);
        foreach (BuiltInRoleDefinition role in (BuiltInRoleDefinition[])[BuiltInRoles.Operator, BuiltInRoles.Moderator, BuiltInRoles.Viewer, BuiltInRoles.SupportDiagnostics])
        {
            await Assert.That(role.Permissions).DoesNotContain(manage);
        }
    }
}
