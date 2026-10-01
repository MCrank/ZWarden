using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tenancy;

/// <summary>
/// Captures a Blazor circuit's tenant once, when the circuit opens (#297, ADR 0046 Q6). The circuit's DI scope is
/// marked as a circuit, so its tenant context never reads the <c>HttpContext</c>; if the circuit's user carries a
/// valid tenant claim, that tenant is assigned for the circuit's lifetime. Otherwise tenant-scoped work in the
/// circuit throws. Circuit handlers run before the circuit's root components render.
/// </summary>
public sealed class TenantCircuitHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _authentication;
    private readonly TenantAssignment _assignment;

    public TenantCircuitHandler(AuthenticationStateProvider authentication, TenantAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(assignment);
        _authentication = authentication;
        _assignment = assignment;
    }

    /// <inheritdoc />
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _assignment.MarkCircuit();
        AuthenticationState state = await _authentication.GetAuthenticationStateAsync().ConfigureAwait(false);
        string? claim = state.User.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value;
        if (state.User.Identity?.IsAuthenticated == true && claim is not null && TenantId.TryParse(claim, out TenantId tenant))
        {
            _assignment.Assign(tenant);
        }
    }
}
