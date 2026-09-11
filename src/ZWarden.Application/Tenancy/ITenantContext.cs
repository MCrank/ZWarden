using ZWarden.Domain.Ids;

namespace ZWarden.Application.Tenancy;

/// <summary>
/// The ambient current tenant (PRD 7A). Its value derives from the authenticated session — never
/// from a request parameter the browser controls (trust-boundaries §2, §6). The tenant filter and
/// the ownership interceptor read it to scope every tenant-owned read and write (ADR 0016).
/// </summary>
/// <remarks>
/// F3A ships one implementation, the self-hosted <see cref="SingleTenantContext"/>. F3B/F4 add the
/// session-derived implementation behind this same interface; no implementation ever accepts a
/// caller-supplied tenant id.
/// </remarks>
public interface ITenantContext
{
    /// <summary>Whether an ambient tenant is currently resolved.</summary>
    bool HasCurrentTenant { get; }

    /// <summary>
    /// The current tenant. <b>Fails closed:</b> throws <see cref="InvalidOperationException"/> when no
    /// ambient tenant is resolved, rather than returning an empty id (which the filter would read as
    /// "match unset rows").
    /// </summary>
    TenantId CurrentTenantId { get; }
}
