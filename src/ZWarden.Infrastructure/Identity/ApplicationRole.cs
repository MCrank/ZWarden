using Microsoft.AspNetCore.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// A ZWarden role, stored by ASP.NET Core Identity (F4). Roles are tenant-global in v1.0 — F5 owns the
/// permission model that gives them meaning; F4 only establishes them as Identity entities so login and
/// the later authorization feature have a role surface to build on.
/// </summary>
/// <remarks>
/// Keyed by a native UUIDv7 <see cref="Guid"/> for the same reason as <see cref="ApplicationUser"/>
/// (Identity's single key CLR type; ADR 0004). The <see cref="RoleId"/> (<c>rol-</c>) typed ID is the
/// domain/API currency, wrapping the stored <see cref="Guid"/>.
/// </remarks>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Creates a role with a fresh time-ordered UUIDv7 key.</summary>
    public ApplicationRole() => Id = Guid.CreateVersion7();

    /// <summary>Creates a named role with a fresh UUIDv7 key.</summary>
    public ApplicationRole(string roleName)
        : this() => Name = roleName;

    /// <summary>The canonical typed identifier (<c>rol-&lt;uuid&gt;</c>).</summary>
    public RoleId RoleId => Domain.Ids.RoleId.FromGuid(Id);
}
