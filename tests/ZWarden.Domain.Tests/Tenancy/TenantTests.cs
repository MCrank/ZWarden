using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Tenancy;

/// <summary>F3A S1: the Tenant entity and the fixed self-hosted default tenant (ADR 0016).</summary>
public class TenantTests
{
    [Test]
    public async Task Default_id_is_a_fixed_well_known_constant()
    {
        // A published, deterministic constant (ADR 0016) - identical across restarts, environments,
        // and runs - so the bootstrap and single-tenant context can name it without a prior read.
        await Assert.That(Tenant.DefaultId).IsEqualTo(Tenant.DefaultId);
        await Assert.That(Tenant.DefaultId.ToString()).IsEqualTo("ten-01920000-0000-7000-8000-000000000001");
        await Assert.That(Tenant.DefaultId.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Default_id_is_a_uuidv7_shaped_value()
    {
        // Version nibble 7, variant bits 10xx - the same shape a generated TenantId would carry.
        byte[] bytes = Tenant.DefaultId.Value.ToByteArray();
        await Assert.That((bytes[7] >> 4) & 0xF).IsEqualTo(7);
    }

    [Test]
    public async Task Create_default_builds_the_default_tenant()
    {
        Tenant tenant = Tenant.CreateDefault();

        await Assert.That(tenant.Id).IsEqualTo(Tenant.DefaultId);
        await Assert.That(tenant.Name).IsEqualTo(Tenant.DefaultName);
    }

    [Test]
    public async Task Tenant_is_not_tenant_owned()
    {
        // A Tenant is not owned by a tenant - it is one (ADR 0016), so it carries no filter.
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Tenant))).IsFalse();
    }
}
