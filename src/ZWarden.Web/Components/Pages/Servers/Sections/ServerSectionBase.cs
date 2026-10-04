using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The base of every Server Detail rail section (#298). The page resolves the Server (tenant-filtered, Server.View)
/// and the per-server permission flags, and renders only the selected section. Each section owns its own forms,
/// handlers and messages; every service a handler calls re-checks the caller's permission fail-closed (ADR 0018).
/// </summary>
/// <remarks>
/// The page is interactive (#299, ADR 0046), so a section lives in a circuit as long as it's shown. Every service
/// call goes through <see cref="Actions"/>, which gives the call a DI scope (and a <c>DbContext</c>) of its own;
/// services injected straight into a section would share the circuit's scope across every event and render.
/// Every <c>BbInput</c> that feeds an action uses <c>UpdateTiming.Immediate</c>: its default reports the value on
/// blur, and a click on Run/Apply can reach the circuit before that report does, so the action would see the old
/// value (the browser smoke tier caught it).
/// </remarks>
public abstract class ServerSectionBase : ComponentBase
{
    /// <summary>The Server the page resolved for the caller.</summary>
    [Parameter, EditorRequired]
    public ServerSummary Server { get; set; } = default!;

    /// <summary>The operator's display time zone, captured by the page while prerendering (the circuit has no
    /// request cookie to read it from).</summary>
    [CascadingParameter(Name = ServerDetailCascade.OperatorZone)]
    protected TimeZoneInfo OperatorZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>Runs each service call in a scope of its own, carrying the circuit's tenant (#297).</summary>
    [Inject]
    protected ActionScopeRunner Actions { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    [Inject]
    private UserManager<ApplicationUser> Users { get; set; } = default!;

    [CascadingParameter(Name = ServerDetailCascade.Navigator)]
    private ServerDetailNavigator? Navigator { get; set; }

    [Inject]
    private NavigationManager Pages { get; set; } = default!;

    /// <summary>The bookmarkable link to another rail section of this Server (#162).</summary>
    protected string SectionHref(string section) => section == "overview"
        ? $"/servers/{Server.Id}"
        : $"/servers/{Server.Id}?section={section}";

    /// <summary>Moves the page to another of this Server's URLs in the circuit (#312), as a click on its link does; a
    /// <c>NavigationManager.NavigateTo</c> would make the server prerender the whole page first.</summary>
    protected async Task GoToAsync(string href)
    {
        if (Navigator is not null)
        {
            await Navigator.GoToAsync(href);
        }
        else
        {
            Pages.NavigateTo(href);
        }
    }

    /// <summary>The signed-in operator, re-read on each action.</summary>
    protected async Task<UserId> CurrentUserAsync()
    {
        ClaimsPrincipal principal = (await AuthState!).User;
        return Guid.TryParse(Users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("Authenticated request carries no user id claim.");
    }
}

/// <summary>Names of the values the Server Detail page cascades to its sections.</summary>
public static class ServerDetailCascade
{
    /// <summary>The operator's display <see cref="TimeZoneInfo"/>.</summary>
    public const string OperatorZone = "zw-operator-zone";

    /// <summary>The page's <see cref="ServerDetailNavigator"/> (#312).</summary>
    public const string Navigator = "zw-server-navigator";
}
