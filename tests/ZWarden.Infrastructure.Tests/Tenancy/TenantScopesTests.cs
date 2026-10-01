using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authorization;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Tenancy;

/// <summary>
/// #297 PR-A: <see cref="TenantScopes"/> is the only way <c>src</c> opens a DI scope, and every scope it opens
/// carries its tenant explicitly - with the session tenant context, which has no request to read here. Offline tier.
/// </summary>
public class TenantScopesTests
{
    [Test]
    public async Task A_tenant_scope_resolves_the_tenant_it_was_opened_for()
    {
        await using ServiceProvider root = SessionHost();
        TenantId tenant = TenantId.New();

        await using AsyncServiceScope scope = root.GetRequiredService<IServiceScopeFactory>().CreateTenantScope(tenant);

        await Assert.That(scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task A_system_scope_resolves_the_installs_default_tenant()
    {
        await using ServiceProvider root = SessionHost();

        await using AsyncServiceScope scope = root.CreateSystemScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId).IsEqualTo(Tenant.DefaultId);
    }

    [Test]
    public async Task A_raw_scope_off_a_session_host_fails_closed()
    {
        await using ServiceProvider root = SessionHost();

        // The pre-#297 behaviour was a silent Tenant.DefaultId here.
        await using AsyncServiceScope scope = root.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        ITenantContext context = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_permission_check_without_a_tenant_is_a_deny_not_an_exception()
    {
        await using ServiceProvider root = SessionHost();
        await using AsyncServiceScope scope = root.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        ScopedPermissionChecker checker = new(
            root.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<ITenantContext>());

        AuthorizationDecision decision = await checker.EvaluateAsync(UserId.New(), Permissions.ServerView, ServerId.New());
        IReadOnlySet<string> tenantWide = await checker.GetTenantWidePermissionsAsync(UserId.New());

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(tenantWide).IsEmpty();
    }

    private static ServiceProvider SessionHost()
    {
        ServiceCollection services = new();
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        return services.BuildServiceProvider();
    }
}
