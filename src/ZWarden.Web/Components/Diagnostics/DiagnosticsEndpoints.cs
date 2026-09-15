using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Diagnostics;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Web.Components.Diagnostics;

/// <summary>
/// The operator-facing diagnostics surface (F29): run a tenant-wide, read-only diagnostics sweep and read back the
/// report. A JSON endpoint under <c>/api</c>, gated by the tenant-wide <c>Diagnostics.View</c> permission (F5) and
/// protected from CSRF by the SameSite=Lax auth cookie. The service re-checks authorization fail-closed (ADR
/// 0018). Every <c>detail</c> field is <b>untrusted</b> (trust-boundaries §8) — the caller escapes it at render.
/// </summary>
public static class DiagnosticsEndpoints
{
    /// <summary>Maps the diagnostics endpoints under <c>/api</c>, behind the tenant-wide view permission.</summary>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints
            .MapGroup("/api")
            .RequireAuthorization(Permissions.DiagnosticsView.Name);

        // Run a read-only diagnostics sweep and return the transient report. The run is non-mutating; the report
        // is not persisted (F29 D-2).
        api.MapPost("/diagnostics/run", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IDiagnosticsService diagnostics,
            CancellationToken cancellationToken) =>
        {
            DiagnosticsRunResult result = await diagnostics
                .RunAsync(Actor(principal, users), cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                // The edge already required Diagnostics.View; a service denial is fail-closed defence in depth.
                return Results.Forbid();
            }

            return Results.Ok(Project(result.Report!));
        });

        // Trigger a read-only host diagnostics gather on an Agent (F29). Non-mutating and host-level, so it takes
        // no per-server lock and carries no ServerId; the Agent runs its host checks and reports the bundle, which
        // the hub caches for the next report read.
        api.MapPost("/agents/{id}/diagnostics/gather", async (
            string id,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IOperationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            if (!AgentId.TryParse(id, out AgentId agentId))
            {
                return Results.BadRequest();
            }

            Operation operation = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.GatherHostDiagnostics, IsMutating: false, Guid.NewGuid().ToString("N")),
                Actor(principal, users),
                cancellationToken).ConfigureAwait(false);

            return Results.Accepted(
                $"/api/operations/{operation.Id}",
                new { operationId = operation.Id.ToString(), state = operation.State.ToString() });
        });

        // Trigger a read-only per-server diagnostics gather (F29). Non-mutating and server-scoped, so it never
        // claims the per-server lock. The Server is resolved through the tenant filter (foreign/unknown ⇒ 404); the
        // gather is dispatched to the Server's owning Agent, which reports the bundle for the hub to cache.
        api.MapPost("/servers/{id}/diagnostics/gather", async (
            string id,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            ServerRepository servers,
            IOperationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            if (!ServerId.TryParse(id, out ServerId serverId))
            {
                return Results.BadRequest();
            }

            var server = await servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (server is null)
            {
                return Results.NotFound();
            }

            Operation operation = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    server.AgentId, OperationKind.GatherServerDiagnostics, IsMutating: false, Guid.NewGuid().ToString("N"),
                    ServerId: serverId),
                Actor(principal, users),
                cancellationToken).ConfigureAwait(false);

            return Results.Accepted(
                $"/api/operations/{operation.Id}",
                new { operationId = operation.Id.ToString(), state = operation.State.ToString() });
        });

        return endpoints;
    }

    private static object Project(DiagnosticReport report) => new
    {
        ranAt = report.RanAt,
        worst = report.Worst().ToString(),
        checks = report.Checks.Select(c => new
        {
            domain = c.Domain.ToString(),
            status = c.Status.ToString(),
            summary = c.Summary,     // ZWarden-authored; safe.
            detail = c.Detail,       // untrusted; escape at render.
        }),
    };

    // The authenticated operator's typed id, from the Identity user-id claim (stamped at sign-in).
    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}
