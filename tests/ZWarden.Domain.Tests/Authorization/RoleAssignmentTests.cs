using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Authorization;

/// <summary>
/// F5 S3 (PR 1): the <see cref="RoleAssignment"/> binding (user → role, optionally narrowed to one
/// Server) is tenant-owned (ADR 0018). Persistence scoping is proven in Infrastructure.Tests; here just
/// the pure invariants.
/// </summary>
public class RoleAssignmentTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Test]
    public async Task Role_assignment_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(RoleAssignment))).IsTrue();
    }

    [Test]
    public async Task A_tenant_wide_assignment_has_no_server_scope()
    {
        RoleAssignment assignment = RoleAssignment.TenantWide(Tenant, UserId.New(), RoleId.New());

        await Assert.That(assignment.ServerId).IsNull();
        await Assert.That(assignment.IsServerScoped).IsFalse();
        await Assert.That(assignment.Id.IsEmpty).IsFalse();
        await Assert.That(assignment.TenantId).IsEqualTo(Tenant);
    }

    [Test]
    public async Task A_server_scoped_assignment_carries_its_server()
    {
        ServerId server = ServerId.New();
        RoleAssignment assignment = RoleAssignment.ForServer(Tenant, UserId.New(), RoleId.New(), server);

        await Assert.That(assignment.IsServerScoped).IsTrue();
        await Assert.That(assignment.ServerId).IsEqualTo(server);
    }
}
