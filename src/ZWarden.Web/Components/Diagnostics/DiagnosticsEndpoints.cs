using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

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
