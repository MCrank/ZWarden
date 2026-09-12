using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ZWarden.Application.Agents;
using ZWarden.Contracts.Enrollment;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Agents;

/// <summary>
/// The operator-facing enrollment/trust surface (F9; ADR 0007) — mint and revoke enrollment tokens, list
/// Agents, and rotate/revoke/disable/enable them. Every route is gated by the
/// <c>Tenant.Enrollment.Manage</c> permission policy (F5); the services re-check it server-side too
/// (defence in depth). These are JSON endpoints protected against CSRF by the SameSite=Lax auth cookie and
/// the permission policy; the guided operator UI is F33. A freshly minted secret/credential is returned in
/// the response exactly once and is never stored raw (decision 2).
/// </summary>
public static class EnrollmentEndpoints
{
    /// <summary>Maps the enrollment/trust endpoints under <c>/api</c>, all behind the manage permission.</summary>
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints
            .MapGroup("/api")
            .RequireAuthorization(Permissions.TenantEnrollmentManage.Name);

        api.MapPost("/enrollments", async (
            IssueEnrollmentRequest? request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IEnrollmentService service,
            CancellationToken cancellationToken) =>
        {
            UserId actor = Actor(principal, users);
            TimeSpan? lifetime = request?.LifetimeMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : null;
            EnrollmentTokenResult result = await service.IssueAsync(actor, lifetime, request?.Label, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                enrollmentId = result.EnrollmentId.ToString(),
                secret = result.Secret.Reveal(),   // shown once — never stored raw, never logged.
                expiresAt = result.ExpiresAt,
                label = result.Label,
            });
        });

        api.MapGet("/enrollments", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IEnrollmentService service,
            CancellationToken cancellationToken) =>
        {
            UserId actor = Actor(principal, users);
            IReadOnlyList<EnrollmentSummary> list = await service.ListAsync(actor, cancellationToken).ConfigureAwait(false);
            return Results.Ok(list.Select(e => new
            {
                id = e.Id.ToString(),
                status = e.Status.ToString(),
                createdAt = e.CreatedAt,
                expiresAt = e.ExpiresAt,
                label = e.Label,
                consumedByAgent = e.ConsumedByAgent?.ToString(),
            }));
        });

        api.MapPost("/enrollments/{id}/revoke", async (
            string id,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IEnrollmentService service,
            CancellationToken cancellationToken) =>
        {
            if (!EnrollmentId.TryParse(id, out EnrollmentId enrollmentId))
            {
                return Results.BadRequest();
            }

            await service.RevokeAsync(Actor(principal, users), enrollmentId, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });

        api.MapGet("/agents", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IAgentTrustService service,
            CancellationToken cancellationToken) =>
        {
            UserId actor = Actor(principal, users);
            IReadOnlyList<AgentSummary> list = await service.ListAsync(actor, cancellationToken).ConfigureAwait(false);
            return Results.Ok(list.Select(a => new
            {
                id = a.Id.ToString(),
                isEnabled = a.IsEnabled,
                hasCredential = a.HasCredential,
                enrolledAt = a.EnrolledAt,
                credentialRotatedAt = a.CredentialRotatedAt,
                label = a.Label,
            }));
        });

        api.MapPost("/agents/{id}/rotate", async (
            string id,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IAgentTrustService service,
            CancellationToken cancellationToken) =>
        {
            if (!AgentId.TryParse(id, out AgentId agentId))
            {
                return Results.BadRequest();
            }

            Domain.Security.SecretString credential =
                await service.RotateCredentialAsync(Actor(principal, users), agentId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { agentId = agentId.ToString(), credential = credential.Reveal() }); // shown once.
        });

        MapAgentAction(api, "revoke", (svc, actor, agentId, ct) => svc.RevokeCredentialAsync(actor, agentId, ct));
        MapAgentAction(api, "disable", (svc, actor, agentId, ct) => svc.DisableAsync(actor, agentId, ct));
        MapAgentAction(api, "enable", (svc, actor, agentId, ct) => svc.EnableAsync(actor, agentId, ct));

        // The pre-trust exchange (F9). Unauthenticated by cookie — the enrollment secret is the
        // authorization. It runs under the ambient (default) tenant, so its lookup is tenant-filtered
        // (no IgnoreQueryFilters). Every failure returns the SAME generic 401 so the endpoint is not an
        // oracle; the specific reason is audited server-side. It reads a JSON body, not a form, so the
        // antiforgery middleware does not apply. Rate-limiting belongs in front of it operationally.
        endpoints.MapPost("/agent/enroll", async (
            EnrollmentRequest? request,
            IAgentEnrollmentExchange exchange,
            CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.EnrollmentSecret))
            {
                return Results.Json(new { error = "enrollment_failed" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            AgentEnrollmentResult result = await exchange
                .RedeemAsync(new SecretString(request.EnrollmentSecret), cancellationToken)
                .ConfigureAwait(false);

            return result.Succeeded
                ? Results.Ok(new EnrollmentResponse(result.AgentId!.Value.ToString(), result.Credential.Reveal(), result.Label))
                : Results.Json(new { error = "enrollment_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous();

        return endpoints;
    }

    private static void MapAgentAction(
        RouteGroupBuilder api,
        string verb,
        Func<IAgentTrustService, UserId, AgentId, CancellationToken, Task> action)
        => api.MapPost($"/agents/{{id}}/{verb}", async (
            string id,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IAgentTrustService service,
            CancellationToken cancellationToken) =>
        {
            if (!AgentId.TryParse(id, out AgentId agentId))
            {
                return Results.BadRequest();
            }

            await action(service, Actor(principal, users), agentId, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });

    // The authenticated operator's typed id, from the Identity user-id claim (stamped at sign-in).
    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}

/// <summary>The body of a mint request: an optional lifetime override (bounded server-side) and label.</summary>
public sealed record IssueEnrollmentRequest(int? LifetimeMinutes, string? Label);
