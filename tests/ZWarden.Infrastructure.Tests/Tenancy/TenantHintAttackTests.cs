using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>
/// F40 release gate — trust-boundaries.md §10 attack 3: <b>the tenant filter under a supplied tenant hint.</b>
/// Tenant context derives from the authenticated session, never from a value the caller chose (PRD 7A;
/// trust-boundaries.md §6). This is the behavioral proof that a request which smuggles a tenant hint — a query
/// string, a header, a route value — is <i>ignored</i>: <see cref="ClaimsPrincipalTenantContext"/> honours only
/// the server-stamped claim. The downstream query-filter and cross-tenant persistence guards are proven
/// elsewhere (TenantIsolationTests, PostgresTenantTests); this seals the entry point they depend on. Offline tier.
/// </summary>
public class TenantHintAttackTests
{
    [Test]
    public async Task A_request_supplied_tenant_hint_is_ignored_in_favour_of_the_session_claim()
    {
        TenantId sessionTenant = TenantId.New();
        TenantId attackerHint = TenantId.New();

        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimsPrincipalTenantContext.TenantClaimType, sessionTenant.ToString())],
                authenticationType: "Test")),
        };

        // The caller tries every channel a request controls to override the tenant.
        context.Request.QueryString = new QueryString($"?tenant={attackerHint}");
        context.Request.Headers["X-Tenant-Id"] = attackerHint.ToString();
        context.Request.Headers[ClaimsPrincipalTenantContext.TenantClaimType] = attackerHint.ToString();
        context.Request.RouteValues["tenant"] = attackerHint.ToString();

        ClaimsPrincipalTenantContext tenantContext = new(new HttpContextAccessor { HttpContext = context });

        // The session claim wins; the hint has no path into the tenant context.
        await Assert.That(tenantContext.CurrentTenantId).IsEqualTo(sessionTenant);
        await Assert.That(tenantContext.CurrentTenantId).IsNotEqualTo(attackerHint);
    }

    [Test]
    public async Task An_anonymous_request_carrying_a_tenant_hint_still_resolves_to_the_default_tenant()
    {
        TenantId attackerHint = TenantId.New();

        DefaultHttpContext context = new(); // no authenticated principal, so no tenant claim
        context.Request.QueryString = new QueryString($"?tenant={attackerHint}");
        context.Request.Headers["X-Tenant-Id"] = attackerHint.ToString();
        context.Request.RouteValues["tenant"] = attackerHint.ToString();

        ClaimsPrincipalTenantContext tenantContext = new(new HttpContextAccessor { HttpContext = context });

        // A pre-auth request cannot elevate itself into another tenant by asking; it is the default tenant.
        await Assert.That(tenantContext.CurrentTenantId).IsEqualTo(Tenant.DefaultId);
        await Assert.That(tenantContext.CurrentTenantId).IsNotEqualTo(attackerHint);
    }
}
