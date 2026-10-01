using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The base of every Server Detail rail section (#298). The page resolves the Server (tenant-filtered, Server.View)
/// and the per-server permission flags, and renders only the selected section. Each section owns its own forms,
/// handlers and messages; every service a handler calls re-checks the caller's permission fail-closed on submit
/// (ADR 0018).
/// </summary>
public abstract class ServerSectionBase : ComponentBase
{
    /// <summary>The Server the page resolved for the caller.</summary>
    [Parameter, EditorRequired]
    public ServerSummary Server { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    [Inject]
    private UserManager<ApplicationUser> Users { get; set; } = default!;

    /// <summary>The bookmarkable link to another rail section of this Server (#162).</summary>
    protected string SectionHref(string section) => section == "overview"
        ? $"/servers/{Server.Id}"
        : $"/servers/{Server.Id}?section={section}";

    /// <summary>The signed-in operator, re-read on each submit.</summary>
    protected async Task<UserId> CurrentUserAsync()
    {
        ClaimsPrincipal principal = (await AuthState!).User;
        return Guid.TryParse(Users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("Authenticated request carries no user id claim.");
    }
}
