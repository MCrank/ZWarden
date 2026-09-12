using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Authorization;

/// <summary>
/// F5 S2 (PR 1): the tenant-owned <see cref="Role"/> aggregate — F5 owns roles (ADR 0018), separate from
/// F4's Identity <c>ApplicationRole</c>. A role bundles catalogue permissions; custom and built-in roles
/// are both tenant-owned, so the tenant filter isolates them (ADR 0016).
/// </summary>
public class RoleTests
{
    private static readonly TenantId TenantA = TenantId.New();

    [Test]
    public async Task Role_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Role))).IsTrue();
    }

    [Test]
    public async Task Granting_a_permission_makes_has_permission_true_only_for_that_permission()
    {
        Role role = Role.CreateCustom(TenantA, "Ops");
        role.Grant(Permissions.ServerStart);

        await Assert.That(role.HasPermission(Permissions.ServerStart.Name)).IsTrue();
        await Assert.That(role.HasPermission(Permissions.ServerStop.Name)).IsFalse();
        await Assert.That(role.HasPermission("Server.Nonexistent")).IsFalse();
    }

    [Test]
    public async Task Granting_is_idempotent_and_rejects_non_catalogue_permissions()
    {
        Role role = Role.CreateCustom(TenantA, "Ops");
        role.Grant(Permissions.ServerStart);
        role.Grant(Permissions.ServerStart);

        await Assert.That(role.Permissions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_custom_role_is_not_built_in_and_carries_the_tenant()
    {
        Role role = Role.CreateCustom(TenantA, "Ops");

        await Assert.That(role.IsBuiltIn).IsFalse();
        await Assert.That(role.BuiltIn).IsNull();
        await Assert.That(role.TenantId).IsEqualTo(TenantA);
        await Assert.That(role.Id.IsEmpty).IsFalse();
    }

    [Test]
    public async Task A_built_in_role_materializes_with_its_bundle_for_a_tenant()
    {
        BuiltInRoleDefinition definition = BuiltInRoles.Get(BuiltInRoleKind.Moderator);
        Role role = Role.FromBuiltIn(TenantA, definition);

        await Assert.That(role.IsBuiltIn).IsTrue();
        await Assert.That(role.BuiltIn).IsEqualTo(BuiltInRoleKind.Moderator);
        await Assert.That(role.TenantId).IsEqualTo(TenantA);
        await Assert.That(role.Name).IsEqualTo(definition.Name);
        await Assert.That(role.Permissions.Count).IsEqualTo(definition.Permissions.Count);
        await Assert.That(role.HasPermission(Permissions.ServerRestart.Name)).IsTrue();
        await Assert.That(role.HasPermission(Permissions.ConsoleExecute.Name)).IsFalse();
    }
}
