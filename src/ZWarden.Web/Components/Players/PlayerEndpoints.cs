using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Players;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Players;

/// <summary>
/// The operator-facing player-management surface (F19): kick, ban, unban, remove-from-whitelist, and the
/// whitelist-mode toggle. Authenticated at the edge; the service is the fail-closed server-scoped gate
/// (<c>Player.Kick</c>/<c>Player.Ban</c>/<c>Player.Unban</c>, and <c>Server.Configuration.Edit</c> for the mode
/// toggle) against the specific Server — a server-scoped policy checked at the endpoint with no resource would
/// deny outright (ADR 0018, the F14 D3 pattern). Each action is a fresh intent enqueued as a non-mutating,
/// server-scoped Operation; the caller polls <c>/api/operations/{id}</c>. Usernames are validated in the service
/// (F19 D-3) before they can reach RCON. JSON endpoints are protected against CSRF by the SameSite=Lax auth
/// cookie; the rich UI is the Blazor page (F19 PR-C). Enumeration is added with that UI.
/// </summary>
public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder players = endpoints.MapGroup("/api/servers/{id}/players").RequireAuthorization();

        players.MapPost("/kick", (string id, PlayerActionRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IPlayerManagement svc, CancellationToken ct) =>
            RunAsync(id, body, principal, users, (u, s) => svc.KickAsync(u, s, body!.Username!, body.Reason, ct)));

        players.MapPost("/ban", (string id, PlayerActionRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IPlayerManagement svc, CancellationToken ct) =>
            RunAsync(id, body, principal, users, (u, s) => svc.BanAsync(u, s, body!.Username!, body.Reason, ct)));

        players.MapPost("/unban", (string id, PlayerActionRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IPlayerManagement svc, CancellationToken ct) =>
            RunAsync(id, body, principal, users, (u, s) => svc.UnbanAsync(u, s, body!.Username!, ct)));

        players.MapPost("/remove-from-whitelist", (string id, PlayerActionRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IPlayerManagement svc, CancellationToken ct) =>
            RunAsync(id, body, principal, users, (u, s) => svc.RemoveFromWhitelistAsync(u, s, body!.Username!, ct)));

        players.MapPost("/whitelist-mode", async (string id, WhitelistModeRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IPlayerManagement svc, CancellationToken ct) =>
        {
            if (body is null || !ServerId.TryParse(id, out ServerId serverId))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            return Map(await svc.SetWhitelistModeAsync(Actor(principal, users), serverId, body.Open, ct).ConfigureAwait(false));
        });

        return endpoints;
    }

    private static async Task<IResult> RunAsync(
        string id,
        PlayerActionRequest? body,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        Func<UserId, ServerId, Task<PlayerManagementResult>> action)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Username) || !ServerId.TryParse(id, out ServerId serverId))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        return Map(await action(Actor(principal, users), serverId).ConfigureAwait(false));
    }

    private static IResult Map(PlayerManagementResult result)
    {
        if (result.Succeeded)
        {
            // Accepted: the Operation is enqueued and runs asynchronously; poll its state for the outcome.
            return Results.Accepted(
                $"/api/operations/{result.Operation!.Value}",
                new { operationId = result.Operation!.Value.ToString() });
        }

        return result.Failure switch
        {
            PlayerManagementFailure.NotAuthorized =>
                Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
            PlayerManagementFailure.ServerNotFound =>
                Results.Json(new { error = "server_not_found" }, statusCode: StatusCodes.Status404NotFound),
            PlayerManagementFailure.InvalidInput =>
                Results.Json(new { error = "invalid_input", detail = result.Detail }, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.BadRequest(new { error = "player_action_failed" }),
        };
    }

    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}

/// <summary>The body of a kick/ban/unban/remove request: the target account username and an optional reason
/// (kick/ban only). Validated in the service before it can reach RCON (F19 D-3).</summary>
public sealed record PlayerActionRequest(string? Username, string? Reason = null);

/// <summary>The body of a whitelist-mode toggle: <c>Open</c> true to admit non-whitelisted players, false to
/// close the server.</summary>
public sealed record WhitelistModeRequest(bool Open);
