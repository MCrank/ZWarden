using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Application.Tenancy;

/// <summary>
/// The self-hosted <see cref="ITenantContext"/>: every request resolves to the fixed default tenant
/// (<see cref="Tenant.DefaultId"/>, ADR 0016). A hosted deployment never uses this; it wires a
/// session-derived context (F3B/F4) behind the same interface. The tenant filter is still evaluated —
/// isolation is not switched off just because there is one tenant (trust-boundaries §6).
/// </summary>
public sealed class SingleTenantContext : ITenantContext
{
    /// <inheritdoc />
    public bool HasCurrentTenant => true;

    /// <inheritdoc />
    public TenantId CurrentTenantId => Tenant.DefaultId;
}
