using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// The operator-facing enrollment surface (PRD 63A; ADR 0007). Every method authorizes the actor against
/// <c>Tenant.Enrollment.Manage</c> (fail-closed via <c>IPermissionChecker</c>) and audits the outcome (F6);
/// no credential is ever placed in an audit record. The minted secret is returned once and stored only as
/// a hash (decision 2).
/// </summary>
public interface IEnrollmentService
{
    /// <summary>Mints a one-time, short-lived enrollment token. <paramref name="lifetime"/> defaults to the
    /// service's configured TTL when null; <paramref name="label"/> is an optional operator note.</summary>
    Task<EnrollmentTokenResult> IssueAsync(
        UserId actor,
        TimeSpan? lifetime = null,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's enrollments (never their secrets or hashes).</summary>
    Task<IReadOnlyList<EnrollmentSummary>> ListAsync(
        UserId actor,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels an unused (pending) enrollment.</summary>
    Task RevokeAsync(
        UserId actor,
        EnrollmentId enrollmentId,
        CancellationToken cancellationToken = default);
}
