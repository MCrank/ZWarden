using Microsoft.AspNetCore.Http;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>
/// The <b>session-derived</b> <see cref="ITenantContext"/> that F3A reserved for F4 (PRD 7A): the
/// current tenant comes from the authenticated principal's tenant claim — stamped server-side at sign-in
/// by <see cref="Identity.TenantClaimsPrincipalFactory"/> — never from a request parameter, header, or
/// route value. It is registered ahead of <c>AddTenantFoundation</c> so it wins over
/// <see cref="SingleTenantContext"/>.
/// </summary>
/// <remarks>
/// In self-hosted v1.0 there is exactly one tenant, so an unauthenticated request (the login page, say)
/// resolves to the <see cref="Tenant.DefaultId">default tenant</see> — that is why the two
/// implementations "agree", and why <see cref="HasCurrentTenant"/> is always true here. The fail-closed
/// "no tenant ⇒ throw" contract bites only in a hosted deployment (F3B, v1.1), which registers a
/// different implementation that does not fall back.
/// </remarks>
public sealed class ClaimsPrincipalTenantContext : ITenantContext
{
    /// <summary>The claim type carrying the canonical tenant id (<c>ten-&lt;uuid&gt;</c>) on the session
    /// principal. Written only by the server at sign-in; the browser never supplies it.</summary>
    public const string TenantClaimType = "zwarden:tenant";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsPrincipalTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool HasCurrentTenant => true;

    /// <inheritdoc />
    public TenantId CurrentTenantId
    {
        get
        {
            string? claim = _httpContextAccessor.HttpContext?.User?.FindFirst(TenantClaimType)?.Value;
            return claim is not null && TenantId.TryParse(claim, out TenantId id)
                ? id
                : Tenant.DefaultId;
        }
    }
}
