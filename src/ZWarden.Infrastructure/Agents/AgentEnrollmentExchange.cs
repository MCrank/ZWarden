using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The pre-trust exchange (PRD 63A; ADR 0007): an unknown Agent presents its one-time enrollment secret and
/// receives a per-Agent credential, creating the trusted Agent record and consuming the enrollment
/// single-use. Not permission-gated — the secret is the authorization. It runs under the ambient (default)
/// tenant, so the lookup is a normal tenant-filtered read (no <c>IgnoreQueryFilters</c>). Every outcome is
/// audited; the specific failure reason is recorded but the caller receives only a generic failure, so the
/// exchange is not an oracle. The <see cref="Enrollment.Version"/> concurrency token makes a concurrent
/// double-redeem resolve to exactly one winner.
/// </summary>
public sealed class AgentEnrollmentExchange : IAgentEnrollmentExchange
{
    private readonly ZWardenDbContext _context;
    private readonly EnrollmentRepository _enrollments;
    private readonly IAuditWriter _audit;
    private readonly ICredentialHasher _hasher;
    private readonly TimeProvider _clock;

    public AgentEnrollmentExchange(
        ZWardenDbContext context,
        EnrollmentRepository enrollments,
        IAuditWriter audit,
        ICredentialHasher hasher,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enrollments);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _enrollments = enrollments;
        _audit = audit;
        _hasher = hasher;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AgentEnrollmentResult> RedeemAsync(
        SecretString presentedSecret,
        CancellationToken cancellationToken = default)
    {
        if (!presentedSecret.HasValue)
        {
            return await FailAsync(EnrollmentRedemptionFailure.UnknownSecret, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        string secretHash = _hasher.Hash(presentedSecret);
        Enrollment? enrollment = await _enrollments.FindBySecretHashAsync(secretHash, cancellationToken).ConfigureAwait(false);

        if (enrollment is null)
        {
            return await FailAsync(EnrollmentRedemptionFailure.UnknownSecret, cancellationToken).ConfigureAwait(false);
        }

        if (!enrollment.IsRedeemable(now))
        {
            EnrollmentRedemptionFailure reason = enrollment.Status switch
            {
                EnrollmentStatus.Consumed => EnrollmentRedemptionFailure.AlreadyConsumed,
                EnrollmentStatus.Revoked => EnrollmentRedemptionFailure.Revoked,
                _ => EnrollmentRedemptionFailure.Expired,
            };
            return await FailAsync(reason, cancellationToken).ConfigureAwait(false);
        }

        SecretString credential = _hasher.Generate(CredentialPrefixes.Agent);
        Agent agent = Agent.Enroll(_hasher.Hash(credential), enrollment.Id, now, enrollment.Label);
        _context.Add(agent);
        enrollment.Consume(agent.Id, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent redeemer consumed the enrollment first; discard this attempt's tracked changes so
            // the audit write below saves cleanly. Single-use holds — this caller loses the race.
            _context.ChangeTracker.Clear();
            return await FailAsync(EnrollmentRedemptionFailure.AlreadyConsumed, cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            new AuditEntry(EnrollmentAuditActions.Redeemed, AuditOutcome.Succeeded, null, null, $"enrollment {enrollment.Id}"),
            cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(EnrollmentAuditActions.AgentEnrolled, AuditOutcome.Succeeded, null, null, $"agent {agent.Id}"),
            cancellationToken).ConfigureAwait(false);

        return AgentEnrollmentResult.Success(agent.Id, credential, enrollment.Label);
    }

    private async Task<AgentEnrollmentResult> FailAsync(
        EnrollmentRedemptionFailure reason,
        CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(
            new AuditEntry(EnrollmentAuditActions.RedemptionFailed, AuditOutcome.Failed, null, null, reason.ToString()),
            cancellationToken).ConfigureAwait(false);
        return AgentEnrollmentResult.Failed(reason);
    }
}
