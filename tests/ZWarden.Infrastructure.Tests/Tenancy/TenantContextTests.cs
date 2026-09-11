using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>F3A S3: the ambient tenant context - the self-hosted default, and the fail-closed contract
/// when no tenant is resolved (ADR 0016).</summary>
public class TenantContextTests
{
    [Test]
    public async Task Single_tenant_context_resolves_to_the_default_tenant()
    {
        SingleTenantContext context = new();

        await Assert.That(context.HasCurrentTenant).IsTrue();
        await Assert.That(context.CurrentTenantId).IsEqualTo(Tenant.DefaultId);
    }

    [Test]
    public async Task A_context_with_no_ambient_tenant_fails_closed()
    {
        TestTenantContext context = new();

        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }
}
