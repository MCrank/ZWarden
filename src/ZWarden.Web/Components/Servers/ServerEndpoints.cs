using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The operator-facing Server inventory + import surface (F14). Reads are gated by <b>authentication only</b>
/// and self-filter server-by-server in the service (a server-scoped <c>Server.View</c> policy checked with no
/// resource would deny outright, ADR 0018) — so the list returns exactly what the caller may view, fail-closed.
/// Import and discovery are gated by the tenant-wide <c>Server.Register</c> policy (F5) and re-checked in the
/// service (defence in depth). JSON endpoints protected against CSRF by the SameSite=Lax auth cookie; the rich
/// dashboard is the Blazor page. All Agent-reported text is untrusted (trust-boundaries.md §3) — escaped at
/// render by the UI.
/// </summary>
public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The inventory list: any authenticated operator; the service returns only Servers the caller may view.
        endpoints.MapGet("/api/servers", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ServerSummary> list =
                await inventory.ListVisibleAsync(Actor(principal, users), cancellationToken).ConfigureAwait(false);
            return Results.Ok(list.Select(ToDto));
        }).RequireAuthorization();

        RouteGroupBuilder register = endpoints
            .MapGroup("/api")
            .RequireAuthorization(Permissions.ServerRegister.Name);

        // The discovered-but-unregistered containers on a host — the import picker's offerings.
        register.MapGet("/agents/{id}/discovered", async (
            string id,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (!AgentId.TryParse(id, out AgentId agentId))
            {
                return Results.BadRequest();
            }

            IReadOnlyList<DiscoveredServer> discovered =
                await inventory.ListDiscoveredUnregisteredAsync(agentId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(discovered.Select(d => new
            {
                serverId = d.ServerId.ToString(),
                runState = d.RunState.ToString(),
            }));
        });

        register.MapPost("/servers", async (
            RegisterServerRequest? request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (request is null
                || !AgentId.TryParse(request.AgentId, out AgentId agentId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            ServerRegisterResult result = await inventory
                .RegisterAsync(Actor(principal, users), agentId, request.Name.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                // Accepted: the record exists; provisioning runs asynchronously as the returned Operation.
                return Results.Accepted(value: new
                {
                    serverId = result.Server!.Value.ToString(),
                    operationId = result.Operation!.Value.ToString(),
                });
            }

            return result.Failure switch
            {
                ServerRegisterFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                ServerRegisterFailure.AgentNotFound =>
                    Results.Json(new { error = "agent_not_found" }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.BadRequest(new { error = "register_failed" }),
            };
        });

        // Lifecycle (F15): start / stop / restart. Authenticated at the edge; the service is the fail-closed
        // server-scoped gate (Server.Start/Stop/Restart against the specific Server) — a server-scoped policy
        // checked at the endpoint with no resource would deny outright (ADR 0018, the F14 D3 pattern). Each is a
        // fresh intent enqueued as a mutating, server-scoped Operation; the caller polls /api/operations/{id}.
        RouteGroupBuilder lifecycle = endpoints.MapGroup("/api/servers").RequireAuthorization();

        lifecycle.MapPost("/{id}/start", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.StartAsync(u, s, ct)));

        lifecycle.MapPost("/{id}/stop", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.StopAsync(u, s, ct)));

        lifecycle.MapPost("/{id}/restart", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.RestartAsync(u, s, ct)));

        // Update (F17): install/validate the PZ install via anonymous SteamCMD — a long, progress-reporting
        // mutating Operation. Same fail-closed server-scoped gate (Server.Update); poll /api/operations/{id}.
        lifecycle.MapPost("/{id}/update", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.UpdateAsync(u, s, ct)));

        register.MapPost("/servers/import", async (
            ImportServerRequest? request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (request is null
                || !AgentId.TryParse(request.AgentId, out AgentId agentId)
                || !ServerId.TryParse(request.ServerId, out ServerId serverId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            ServerImportResult result = await inventory
                .ImportAsync(Actor(principal, users), agentId, serverId, request.Name.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                return Results.Ok(new { serverId = result.Server!.Value.ToString() });
            }

            return result.Failure switch
            {
                ServerImportFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                ServerImportFailure.AgentNotFound =>
                    Results.Json(new { error = "agent_not_found" }, statusCode: StatusCodes.Status404NotFound),
                ServerImportFailure.NotDiscovered =>
                    Results.Json(new { error = "not_discovered" }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.BadRequest(new { error = "import_failed" }),
            };
        });

        return endpoints;
    }

    private static async Task<IResult> RunLifecycleAsync(
        string id,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        Func<UserId, ServerId, Task<ServerLifecycleResult>> action)
    {
        if (!ServerId.TryParse(id, out ServerId serverId))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        ServerLifecycleResult result = await action(Actor(principal, users), serverId).ConfigureAwait(false);
        if (result.Succeeded)
        {
            // Accepted: the Operation is enqueued and runs asynchronously; poll its state.
            return Results.Accepted(
                $"/api/operations/{result.Operation!.Value}",
                new { operationId = result.Operation!.Value.ToString() });
        }

        return result.Failure switch
        {
            ServerLifecycleFailure.NotAuthorized =>
                Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
            ServerLifecycleFailure.ServerNotFound =>
                Results.Json(new { error = "server_not_found" }, statusCode: StatusCodes.Status404NotFound),
            ServerLifecycleFailure.ServerBusy =>
                Results.Json(new { error = "server_busy" }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.BadRequest(new { error = "lifecycle_failed" }),
        };
    }

    private static object ToDto(ServerSummary s) => new
    {
        id = s.Id.ToString(),
        agentId = s.AgentId.ToString(),
        name = s.Name,
        description = s.Description,
        gamePort = s.GamePort,
        queryPort = s.QueryPort,
        lastRunState = s.LastRunState.ToString(),
        lastStateReportedAt = s.LastStateReportedAt,
        installedBuildId = s.InstalledBuildId,
    };

    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}

/// <summary>The body of an import request: which host, which discovered Server id, and an operator name.</summary>
public sealed record ImportServerRequest(string AgentId, string ServerId, string Name);

/// <summary>The body of a register request: which host to provision the new Server on, and its name.</summary>
public sealed record RegisterServerRequest(string AgentId, string Name);
