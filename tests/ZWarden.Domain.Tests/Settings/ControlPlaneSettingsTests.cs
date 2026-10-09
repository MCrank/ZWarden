using ZWarden.Domain.Ids;
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
    public async Task The_session_idle_timeout_offers_bounded_choices_around_the_8_hour_default()
    {
        await Assert.That(ControlPlaneSettings.DefaultSessionIdleTimeout).IsEqualTo(TimeSpan.FromHours(8));
        await Assert.That(ControlPlaneSettings.SessionIdleTimeoutChoices).IsEquivalentTo(
            [TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromHours(24), TimeSpan.FromDays(7)]);
        await Assert.That(ControlPlaneSettings.Create().SessionIdleTimeout).IsNull();
    }

    [Test]
    public async Task A_listed_session_timeout_is_stored_and_the_default_stores_nothing()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        settings.SetSessionIdleTimeout(TimeSpan.FromMinutes(30));
        await Assert.That(settings.SessionIdleTimeout).IsEqualTo(TimeSpan.FromMinutes(30));

        settings.SetSessionIdleTimeout(TimeSpan.FromHours(8));
        await Assert.That(settings.SessionIdleTimeout).IsNull();

        settings.SetSessionIdleTimeout(TimeSpan.FromDays(7));
        settings.SetSessionIdleTimeout(null);
        await Assert.That(settings.SessionIdleTimeout).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(5)]
    [Arguments(45)]
    [Arguments(60 * 24 * 30)]
    public async Task A_session_timeout_off_the_list_is_refused(int minutes)
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();
        settings.SetSessionIdleTimeout(TimeSpan.FromHours(1));

        await Assert.That(() => settings.SetSessionIdleTimeout(TimeSpan.FromMinutes(minutes))).Throws<ArgumentException>();
        await Assert.That(settings.SessionIdleTimeout).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task The_default_deploy_host_is_set_and_cleared()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();
        AgentId host = AgentId.New();

        await Assert.That(settings.DefaultDeployHost).IsNull();
        settings.SetDefaultDeployHost(host);
        await Assert.That(settings.DefaultDeployHost).IsEqualTo(host);
        settings.SetDefaultDeployHost(null);
        await Assert.That(settings.DefaultDeployHost).IsNull();
    }

    [Test]
    public async Task An_empty_host_id_is_refused()
    {
        ControlPlaneSettings settings = ControlPlaneSettings.Create();

        await Assert.That(() => settings.SetDefaultDeployHost(default(AgentId))).Throws<ArgumentException>();
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
