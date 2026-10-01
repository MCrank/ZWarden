using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// The <b>session-derived</b> <see cref="ITenantContext"/> (PRD 7A). The current tenant comes from the
/// authenticated principal's tenant claim, stamped server-side at sign-in by
/// <see cref="Identity.TenantClaimsPrincipalFactory"/> (or, for an Agent, by its authentication handler), never
/// from a request parameter, header, or route value. It is registered ahead of <c>AddTenantFoundation</c> so it
/// wins over <see cref="SingleTenantContext"/>.
/// </summary>
/// <remarks>
/// <b>Fails closed</b> (#297, ADR 0046 Q6). Resolution order:
/// <list type="number">
/// <item>a tenant explicitly assigned to this scope (<see cref="TenantAssignment"/>);</item>
/// <item>a Blazor circuit with no assigned tenant throws (its <c>HttpContext</c> is never trusted);</item>
/// <item>no <c>HttpContext</c> throws;</item>
/// <item>an authenticated principal must carry a valid tenant claim, or it throws;</item>
/// <item>an <b>anonymous request</b> (login, setup, health, enrollment exchange) belongs to the install's default
/// tenant. That is the self-hosted rule; a hosted deployment (F3B) resolves it from the request host instead.</item>
/// </list>
/// </remarks>
public sealed class ClaimsPrincipalTenantContext : ITenantContext
{
    /// <summary>The claim type carrying the canonical tenant id (<c>ten-&lt;uuid&gt;</c>) on the session
    /// principal. Written only by the server; the browser never supplies it.</summary>
    public const string TenantClaimType = "zwarden:tenant";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantAssignment _assignment;

    public ClaimsPrincipalTenantContext(IHttpContextAccessor httpContextAccessor, TenantAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(assignment);
        _httpContextAccessor = httpContextAccessor;
        _assignment = assignment;
    }

    /// <inheritdoc />
    public bool HasCurrentTenant => TryResolve(out _, out _);

    /// <inheritdoc />
    public TenantId CurrentTenantId =>
        TryResolve(out TenantId tenant, out string? reason) ? tenant : throw new InvalidOperationException(reason);

    private bool TryResolve(out TenantId tenant, [NotNullWhen(false)] out string? reason)
    {
        if (_assignment.Tenant is { } assigned)
        {
            tenant = assigned;
            reason = null;
            return true;
        }

        tenant = default;
        if (_assignment.IsCircuit)
        {
            reason = "No tenant was captured for this circuit; tenant-scoped work in a circuit fails closed.";
            return false;
        }

        HttpContext? http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            reason = "No tenant: this scope has no request and no assigned tenant. Open it with TenantScopes.";
            return false;
        }

        if (http.User.Identities.Any(i => i.IsAuthenticated))
        {
            string? claim = http.User.FindFirst(TenantClaimType)?.Value;
            if (claim is not null && TenantId.TryParse(claim, out tenant))
            {
                reason = null;
                return true;
            }

            reason = "The authenticated principal carries no valid tenant claim.";
            return false;
        }

        // The named anonymous-request rule (self-hosted): an unauthenticated request belongs to the install.
        tenant = Tenant.DefaultId;
        reason = null;
        return true;
    }
}
