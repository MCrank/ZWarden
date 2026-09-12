using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S6/S7 (PR 2): the ASP.NET Core enforcement surface (ADR 0018) — the dynamic policy provider maps any
/// catalogue permission name to a policy, and the handlers satisfy it from the fail-closed
/// <see cref="IPermissionChecker"/> (tenant-wide, and against a server-scoped resource). Offline tier.
/// </summary>
public class AuthorizationPolicyTests
{
    private sealed class FakeChecker : IPermissionChecker
    {
        private readonly bool _allow;

        public FakeChecker(bool allow) => _allow = allow;

        public Task<AuthorizationDecision> EvaluateAsync(
            UserId user, PermissionDefinition permission, ServerId? server = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_allow
                ? AuthorizationDecision.Allow(permission, server)
                : AuthorizationDecision.Deny("denied"));

        public Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(UserId user, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed record ServerResource(ServerId ServerId) : IServerScoped;

    private static ClaimsPrincipal AuthenticatedUser() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "test"));

    private static IOptions<IdentityOptions> IdentityOptions() => Options.Create(new IdentityOptions());

    [Test]
    public async Task Provider_maps_a_catalogue_permission_name_to_a_requirement()
    {
        PermissionPolicyProvider provider = new(Options.Create(new AuthorizationOptions()));

        AuthorizationPolicy? policy = await provider.GetPolicyAsync("Server.Start");

        await Assert.That(policy).IsNotNull();
        PermissionRequirement requirement = policy!.Requirements.OfType<PermissionRequirement>().Single();
        await Assert.That(requirement.Permission.Name).IsEqualTo("Server.Start");
    }

    [Test]
    public async Task Provider_returns_null_for_a_non_permission_policy_name()
    {
        PermissionPolicyProvider provider = new(Options.Create(new AuthorizationOptions()));

        await Assert.That(await provider.GetPolicyAsync("Not.A.Real.Permission")).IsNull();
    }

    [Test]
    public async Task Tenant_wide_handler_succeeds_when_the_checker_allows()
    {
        PermissionAuthorizationHandler handler = new(new FakeChecker(allow: true), IdentityOptions());
        PermissionRequirement requirement = new(Permissions.RoleManage);
        AuthorizationHandlerContext context = new([requirement], AuthenticatedUser(), resource: null);

        await handler.HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsTrue();
    }

    [Test]
    public async Task Tenant_wide_handler_does_not_succeed_when_the_checker_denies()
    {
        PermissionAuthorizationHandler handler = new(new FakeChecker(allow: false), IdentityOptions());
        PermissionRequirement requirement = new(Permissions.RoleManage);
        AuthorizationHandlerContext context = new([requirement], AuthenticatedUser(), resource: null);

        await handler.HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsFalse();
    }

    [Test]
    public async Task Handler_does_not_succeed_without_an_authenticated_user()
    {
        PermissionAuthorizationHandler handler = new(new FakeChecker(allow: true), IdentityOptions());
        PermissionRequirement requirement = new(Permissions.RoleManage);
        // An anonymous principal carries no user-id claim.
        AuthorizationHandlerContext context = new([requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        await handler.HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsFalse();
    }

    [Test]
    public async Task Server_scoped_handler_succeeds_when_the_checker_allows_for_the_resource_server()
    {
        ServerScopedPermissionHandler handler = new(new FakeChecker(allow: true), IdentityOptions());
        PermissionRequirement requirement = new(Permissions.ServerStart);
        ServerResource resource = new(ServerId.New());
        AuthorizationHandlerContext context = new([requirement], AuthenticatedUser(), resource);

        await handler.HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsTrue();
    }
}
