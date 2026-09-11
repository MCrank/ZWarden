using Microsoft.AspNetCore.Identity;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The ZWarden user, stored by ASP.NET Core Identity (F4). It is <b>tenant-owned</b> (PRD 7A): every
/// user belongs to exactly one Tenant, carried in an immutable <see cref="TenantId"/> that the tenant
/// filter scopes every read by and the ownership interceptor stamps on insert (ADR 0016).
/// </summary>
/// <remarks>
/// The primary key is a native UUIDv7 <see cref="Guid"/> (ADR 0004 — typed IDs are stored as native
/// uuid): Identity's EF store uses a single key CLR type across users and roles, so the distinct
/// <see cref="Ids.UserId"/> (<c>usr-</c>) and <see cref="RoleId"/> (<c>rol-</c>) typed IDs cannot both
/// be the EF key. The typed ID stays the domain/API/log currency, wrapped over the stored
/// <see cref="Guid"/> by <see cref="UserId"/> — the raw uuid is never surfaced (CONTEXT.md).
/// </remarks>
public class ApplicationUser : IdentityUser<Guid>, ITenantOwned
{
    /// <summary>Creates a user with a fresh time-ordered UUIDv7 key (ADR 0014's ordering property).</summary>
    public ApplicationUser() => Id = Guid.CreateVersion7();

    /// <summary>Creates a user with the given user name and a fresh UUIDv7 key.</summary>
    public ApplicationUser(string userName)
        : this() => UserName = userName;

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The canonical typed identifier (<c>usr-&lt;uuid&gt;</c>) — the form used everywhere the
    /// user is referenced outside the Identity store; the stored <see cref="IdentityUser{TKey}.Id"/>
    /// Guid is never surfaced directly.</summary>
    public UserId UserId => Domain.Ids.UserId.FromGuid(Id);
}
