using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>
/// The model-level half of the §9 rule-4 guard (ADR 0016): every <see cref="ITenantOwned"/> entity in
/// the built model carries a query filter. A tenant-owned type that reached the model without one -
/// through a convention regression - is a red build here. Offline tier.
/// </summary>
public class TenantFilterModelGuardTests
{
    [Test]
    public async Task Every_tenant_owned_entity_has_a_query_filter()
    {
        DbContextOptions options = new DbContextOptionsBuilder<TenantTestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, "Data Source=:memory:")
            .Options;
        await using TenantTestDbContext db = new(options, new TestTenantContext(TenantId.New()));

        IEntityType[] tenantOwned = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .ToArray();

        // Sanity: the model really does contain a tenant-owned entity, so the assertion is not vacuous.
        await Assert.That(tenantOwned.Length).IsGreaterThan(0);

        foreach (IEntityType entity in tenantOwned)
        {
            await Assert.That(entity.GetDeclaredQueryFilters().Count).IsGreaterThan(0);
        }
    }

    /// <summary>F4: the Identity user is tenant-owned, so a user is always scoped to a tenant. Removing
    /// <see cref="ITenantOwned"/> from <see cref="ApplicationUser"/> - dropping its tenant scope - is a
    /// red build here, not a silent cross-tenant leak.</summary>
    [Test]
    public async Task The_application_user_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(ApplicationUser))).IsTrue();
    }
}
