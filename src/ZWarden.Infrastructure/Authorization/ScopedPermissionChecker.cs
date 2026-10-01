using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// An <see cref="IPermissionChecker"/> that runs each check inside its own short-lived DI scope, so every
/// evaluation gets a fresh <c>ZWardenDbContext</c> rather than sharing the ambient (per-request or, under
/// interactive Blazor Server, per-circuit) one. Authorization is evaluated during rendering — a layout's
/// permission-gated nav, an <c>AuthorizeView</c>, and a page's own checks can all run concurrently on one
/// circuit — and EF Core forbids two operations on a single context instance at once ("A second operation
/// was started on this context…"). Isolating each check removes that contention while keeping the decision
/// logic in <see cref="PermissionChecker"/> pure and directly unit-testable.
/// </summary>
/// <remarks>
/// The child scope carries the caller's tenant explicitly (<see cref="TenantScopes"/>, #297): a circuit has no
/// HttpContext to read it from, and the tenant context fails closed rather than defaulting.
/// </remarks>
public sealed class ScopedPermissionChecker : IPermissionChecker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantContext _tenant;

    public ScopedPermissionChecker(IServiceScopeFactory scopeFactory, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(tenant);
        _scopeFactory = scopeFactory;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task<AuthorizationDecision> EvaluateAsync(
        UserId user,
        PermissionDefinition permission,
        ServerId? server = null,
        CancellationToken cancellationToken = default)
    {
        // No tenant is a Deny, as in PermissionChecker itself, never an exception from the scope.
        if (!_tenant.HasCurrentTenant)
        {
            return AuthorizationDecision.Deny("No authenticated tenant.");
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(_tenant.CurrentTenantId);
        PermissionChecker inner = scope.ServiceProvider.GetRequiredService<PermissionChecker>();
        return await inner.EvaluateAsync(user, permission, server, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(
        UserId user,
        CancellationToken cancellationToken = default)
    {
        if (!_tenant.HasCurrentTenant)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(_tenant.CurrentTenantId);
        PermissionChecker inner = scope.ServiceProvider.GetRequiredService<PermissionChecker>();
        return await inner.GetTenantWidePermissionsAsync(user, cancellationToken).ConfigureAwait(false);
    }
}
