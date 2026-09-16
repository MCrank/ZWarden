using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

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
/// Tenant context still resolves correctly in the child scope: under an HTTP request the
/// <c>IHttpContextAccessor</c> flows via <c>AsyncLocal</c>, and under an interactive circuit there is no
/// HttpContext, so it falls back to the single default tenant (ADR 0016) — the same value the ambient scope
/// would have produced.
/// </remarks>
public sealed class ScopedPermissionChecker : IPermissionChecker
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedPermissionChecker(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<AuthorizationDecision> EvaluateAsync(
        UserId user,
        PermissionDefinition permission,
        ServerId? server = null,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        PermissionChecker inner = scope.ServiceProvider.GetRequiredService<PermissionChecker>();
        return await inner.EvaluateAsync(user, permission, server, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(
        UserId user,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        PermissionChecker inner = scope.ServiceProvider.GetRequiredService<PermissionChecker>();
        return await inner.GetTenantWidePermissionsAsync(user, cancellationToken).ConfigureAwait(false);
    }
}
