using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// Runs one action from an interactive page in its own DI scope, carrying the caller's tenant (#297, ADR 0046
/// Q7). The circuit's own scope lives as long as the tab, so services resolved from it would share one
/// <c>ZWardenDbContext</c> across every event, timer and render, and EF forbids concurrent use of a context
/// (the #154 bug class). A scope per action gives that action a context of its own, including the one behind
/// <c>UserManager</c>, without changing the services.
/// </summary>
/// <remarks>
/// Interactive components inject this and call the service through it:
/// <code>await Actions.RunAsync&lt;IServerLifecycle, ServerLifecycleResult&gt;((s, ct) =&gt; s.StartAsync(user, id, ct), ct);</code>
/// Don't return a tracked entity, an <c>IQueryable</c> or the service itself from the action: they die with the
/// scope. Return plain results.
/// </remarks>
public sealed class ActionScopeRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantContext _tenant;

    public ActionScopeRunner(IServiceScopeFactory scopeFactory, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(tenant);
        _scopeFactory = scopeFactory;
        _tenant = tenant;
    }

    /// <summary>Resolves <typeparamref name="TService"/> in a fresh tenant scope and runs <paramref name="action"/>.</summary>
    public async Task<TResult> RunAsync<TService, TResult>(
        Func<TService, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(_tenant.CurrentTenantId);
        TService service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="RunAsync{TService, TResult}"/>
    public async Task RunAsync<TService>(
        Func<TService, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(_tenant.CurrentTenantId);
        TService service = scope.ServiceProvider.GetRequiredService<TService>();
        await action(service, cancellationToken).ConfigureAwait(false);
    }
}
