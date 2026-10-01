using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Agents;

/// <summary>
/// Gives every <see cref="AgentHub"/> invocation and lifecycle scope its tenant explicitly (#297, ADR 0046 Q6):
/// the tenant claim <see cref="AgentAuthenticationHandler"/> stamped at the handshake is assigned into that scope's
/// <see cref="TenantAssignment"/>. Hub work therefore never depends on the <c>HttpContext</c> accessor surviving
/// the long-lived connection. A principal without a valid tenant claim fails closed.
/// </summary>
public sealed class AgentTenantHubFilter : IHubFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);
        Assign(invocationContext.ServiceProvider, invocationContext.Context.User);
        return next(invocationContext);
    }

    /// <inheritdoc />
    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        Assign(context.ServiceProvider, context.Context.User);
        return next(context);
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        Assign(context.ServiceProvider, context.Context.User);
        return next(context, exception);
    }

    /// <summary>Assigns the principal's tenant claim into <paramref name="scope"/>; throws if it has none.</summary>
    public static void Assign(IServiceProvider scope, ClaimsPrincipal? principal)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string? claim = principal?.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value;
        if (claim is null || !TenantId.TryParse(claim, out TenantId tenant))
        {
            throw new HubException("The Agent connection carries no valid tenant.");
        }

        scope.GetRequiredService<TenantAssignment>().Assign(tenant);
    }
}
