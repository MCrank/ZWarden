using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Operations;

/// <summary>
/// The minimal operator-facing operations surface (F11): enqueue a <c>Diagnostics.Ping</c> against an Agent
/// and read an Operation's state. JSON endpoints under <c>/api</c>, gated by the existing
/// <c>Agent.Manage</c> permission (F5) and protected from CSRF by the SameSite=Lax auth cookie. The rich
/// operations UI and history viewer are F14/F16; this is the smallest surface that makes the engine
/// operator-runnable now. Agent-reported <c>statusLine</c>/<c>failureReason</c> are untrusted display text
/// (trust-boundaries.md §3) — the caller escapes them at render.
/// </summary>
public static class OperationEndpoints
{
    /// <summary>Maps the operations endpoints under <c>/api</c>, behind the manage permission.</summary>
    public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints
            .MapGroup("/api")
            .RequireAuthorization(Permissions.AgentManage.Name);

        // Enqueue a diagnostic ping. Each request is a new intent, so the idempotency key is fresh; the
        // coordinator dispatches it to the Agent if connected (else it stays Pending). Non-mutating, so it
        // takes no per-server lock.
        api.MapPost("/agents/{id}/ping", async (
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
                    agentId, OperationKind.DiagnosticsPing, IsMutating: false, Guid.NewGuid().ToString("N")),
                Actor(principal, users),
                cancellationToken).ConfigureAwait(false);

            return Results.Accepted(
                $"/api/operations/{operation.Id}",
                new { operationId = operation.Id.ToString(), state = operation.State.ToString() });
        });

        api.MapGet("/operations/{id}", async (
            string id,
            IOperationStore store,
            CancellationToken cancellationToken) =>
        {
            if (!OperationId.TryParse(id, out OperationId operationId))
            {
                return Results.BadRequest();
            }

            Operation? operation = await store.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
            return operation is null ? Results.NotFound() : Results.Ok(Project(operation));
        });

        return endpoints;
    }

    private static object Project(Operation operation) => new
    {
        id = operation.Id.ToString(),
        kind = operation.Kind.ToString(),
        state = operation.State.ToString(),
        agentId = operation.AgentId.ToString(),
        serverId = operation.ServerId?.ToString(),
        percentComplete = operation.PercentComplete,
        statusLine = operation.StatusLine,       // untrusted; escape at render.
        failureReason = operation.FailureReason, // untrusted; escape at render.
        enqueuedAt = operation.EnqueuedAt,
        startedAt = operation.StartedAt,
        completedAt = operation.CompletedAt,
    };

    // The authenticated operator's typed id, from the Identity user-id claim (stamped at sign-in).
    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}
