using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>
/// #297 PR-A (ADR 0046 Q6): <see cref="ClaimsPrincipalTenantContext"/> fails closed. The default tenant is reached
/// only through named paths - an explicit <see cref="TenantAssignment"/> (system scope, circuit capture, Agent hub
/// filter) or the anonymous-HTTP-request rule - never because something was missing. Offline tier.
/// </summary>
public class FailClosedTenantContextTests
{
    [Test]
    public async Task An_assigned_tenant_wins_over_the_request_principal()
    {
        TenantId assigned = TenantId.New();
        TenantAssignment assignment = new();
        assignment.Assign(assigned);
        ClaimsPrincipalTenantContext context = new(Accessor(SignedIn(TenantId.New())), assignment);

        await Assert.That(context.CurrentTenantId).IsEqualTo(assigned);
        await Assert.That(context.HasCurrentTenant).IsTrue();
    }

    [Test]
    public async Task A_scope_with_no_http_context_and_no_assignment_throws()
    {
        ClaimsPrincipalTenantContext context = new(new HttpContextAccessor(), new TenantAssignment());

        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_circuit_without_a_captured_tenant_throws_even_when_an_http_context_is_visible()
    {
        TenantAssignment assignment = new();
        assignment.MarkCircuit();
        // An anonymous HttpContext would otherwise resolve to the default tenant via the anonymous rule.
        ClaimsPrincipalTenantContext context = new(Accessor(new ClaimsPrincipal(new ClaimsIdentity())), assignment);

        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_circuit_with_a_captured_tenant_resolves_it()
    {
        TenantId captured = TenantId.New();
        TenantAssignment assignment = new();
        assignment.MarkCircuit();
        assignment.Assign(captured);
        ClaimsPrincipalTenantContext context = new(new HttpContextAccessor(), assignment);

        await Assert.That(context.CurrentTenantId).IsEqualTo(captured);
    }

    [Test]
    public async Task A_signed_in_principal_without_a_tenant_claim_throws()
    {
        ClaimsPrincipal noClaim = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u")], "Test"));
        ClaimsPrincipalTenantContext context = new(Accessor(noClaim), new TenantAssignment());

        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_signed_in_principal_with_an_unparseable_tenant_claim_throws()
    {
        ClaimsPrincipal garbage = new(new ClaimsIdentity(
            [new Claim(ClaimsPrincipalTenantContext.TenantClaimType, "not-a-tenant")], "Test"));
        ClaimsPrincipalTenantContext context = new(Accessor(garbage), new TenantAssignment());

        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_signed_in_principal_resolves_its_own_claim()
    {
        TenantId tenant = TenantId.New();
        ClaimsPrincipalTenantContext context = new(Accessor(SignedIn(tenant)), new TenantAssignment());

        await Assert.That(context.CurrentTenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task An_anonymous_request_belongs_to_the_installs_default_tenant()
    {
        ClaimsPrincipalTenantContext context = new(Accessor(new ClaimsPrincipal(new ClaimsIdentity())), new TenantAssignment());

        await Assert.That(context.CurrentTenantId).IsEqualTo(Tenant.DefaultId);
    }

    [Test]
    public async Task Reassigning_the_same_tenant_is_a_no_op_but_a_different_tenant_throws()
    {
        TenantId tenant = TenantId.New();
        TenantAssignment assignment = new();
        assignment.Assign(tenant);
        assignment.Assign(tenant);

        await Assert.That(() => assignment.Assign(TenantId.New())).Throws<InvalidOperationException>();
        await Assert.That(assignment.Tenant).IsEqualTo(tenant);
    }

    private static ClaimsPrincipal SignedIn(TenantId tenant) =>
        new(new ClaimsIdentity([new Claim(ClaimsPrincipalTenantContext.TenantClaimType, tenant.ToString())], "Test"));

    private static HttpContextAccessor Accessor(ClaimsPrincipal user) =>
        new() { HttpContext = new DefaultHttpContext { User = user } };
}
