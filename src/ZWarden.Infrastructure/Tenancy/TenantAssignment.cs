using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// The tenant explicitly given to one DI scope (#297, ADR 0046 Q6). <see cref="ClaimsPrincipalTenantContext"/>
/// reads it before anything else. It is set by the named paths only: <see cref="TenantScopes"/> (system and
/// tenant-carrying scopes), the circuit tenant capture, and the Agent hub filter.
/// </summary>
/// <remarks>
/// Assigned once: re-assigning the same tenant is a no-op, a different one throws, so nothing downstream can
/// switch a scope's tenant. <see cref="MarkCircuit"/> records that the scope is a Blazor circuit, where the
/// <c>HttpContext</c> is never a valid source - an unassigned circuit fails closed instead of falling through
/// to the anonymous-request rule.
/// </remarks>
public sealed class TenantAssignment
{
    private TenantId? _tenant;

    /// <summary>The assigned tenant, or <c>null</c> when none was given to this scope.</summary>
    public TenantId? Tenant => _tenant;

    /// <summary>Whether this scope is a Blazor circuit (see <see cref="MarkCircuit"/>).</summary>
    public bool IsCircuit { get; private set; }

    /// <summary>Assigns <paramref name="tenant"/> to this scope. Throws if a different tenant is already assigned.</summary>
    public void Assign(TenantId tenant)
    {
        if (_tenant is { } existing && existing != tenant)
        {
            throw new InvalidOperationException("This scope already has a different tenant; a scope's tenant never changes.");
        }

        _tenant = tenant;
    }

    /// <summary>Marks this scope as a Blazor circuit: its tenant comes only from <see cref="Assign"/>.</summary>
    public void MarkCircuit() => IsCircuit = true;
}
