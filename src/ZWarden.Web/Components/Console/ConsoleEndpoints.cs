using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Console;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Console;

/// <summary>
/// The operator-facing remote-console surface (F28): submit one RCON command line to run against a Server.
/// Authenticated at the edge; the service is the fail-closed, server-scoped gate on the <b>elevated</b>
/// <c>Console.Execute</c> permission against the specific Server — a server-scoped policy checked at the endpoint
/// with no resource would deny outright (ADR 0018, the F14 D3 pattern). Each submission is a fresh intent enqueued
/// as a non-mutating, server-scoped Operation; the caller polls <c>/api/operations/{id}</c> and reads the observed
/// output from the live pane (the in-memory cache). The command is validated and policy-checked in the service
/// (F28 D-1 / ADR 0032) before it can reach RCON. The JSON endpoint is protected against CSRF by the SameSite=Lax
/// auth cookie; the rich UI is the Blazor page (F28 PR-C).
/// </summary>
public static class ConsoleEndpoints
{
    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder console = endpoints.MapGroup("/api/servers/{id}/console").RequireAuthorization();

        console.MapPost(string.Empty, async (string id, ConsoleCommandRequest? body, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IConsoleCommandService svc, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Input) || !ServerId.TryParse(id, out ServerId serverId))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            return Map(await svc.ExecuteAsync(Actor(principal, users), serverId, body.Input, ct).ConfigureAwait(false));
        });

        return endpoints;
    }

    private static IResult Map(ConsoleExecutionResult result)
    {
        if (result.Succeeded)
        {
            // Accepted: the Operation is enqueued and runs asynchronously; poll its state for the outcome and read
            // the observed output from the live console pane.
            return Results.Accepted(
                $"/api/operations/{result.Operation!.Value}",
                new { operationId = result.Operation!.Value.ToString() });
        }

        return result.Failure switch
        {
            ConsoleCommandFailure.NotAuthorized =>
                Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
            ConsoleCommandFailure.ServerNotFound =>
                Results.Json(new { error = "server_not_found" }, statusCode: StatusCodes.Status404NotFound),
            ConsoleCommandFailure.InvalidInput =>
                Results.Json(new { error = "invalid_input", detail = result.Detail }, statusCode: StatusCodes.Status400BadRequest),
            ConsoleCommandFailure.Denied =>
                Results.Json(new { error = "command_denied", detail = result.Detail }, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.BadRequest(new { error = "console_command_failed" }),
        };
    }

    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}

/// <summary>The body of a console command submission: the operator-authored RCON command line. Validated and
/// policy-checked in the service (F28 D-1 / ADR 0032) before it can reach RCON.</summary>
public sealed record ConsoleCommandRequest(string? Input);
