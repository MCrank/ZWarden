using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// The operator-facing enrollment surface (PRD 63A; ADR 0007). Every method requires the actor to hold
/// <c>Tenant.Enrollment.Manage</c> tenant-wide (fail-closed) and audits the outcome (F6); no credential is
/// ever written to an audit record. A minted secret is returned once and persisted only as a hash
/// (decision 2). Enrollments are tenant-owned — the ownership interceptor stamps the ambient tenant and the
/// repository reads through the tenant filter (ADR 0016).
/// </summary>
public sealed class EnrollmentService : IEnrollmentService
{
    private readonly ZWardenDbContext _context;
    private readonly EnrollmentRepository _enrollments;
    private readonly IPermissionChecker _checker;
    private readonly IAuditWriter _audit;
    private readonly ICredentialHasher _hasher;
    private readonly TimeProvider _clock;
    private readonly EnrollmentOptions _options;

    public EnrollmentService(
        ZWardenDbContext context,
        EnrollmentRepository enrollments,
        IPermissionChecker checker,
        IAuditWriter audit,
        ICredentialHasher hasher,
        TimeProvider clock,
        IOptions<EnrollmentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enrollments);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _enrollments = enrollments;
        _checker = checker;
        _audit = audit;
        _hasher = hasher;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<EnrollmentTokenResult> IssueAsync(
        UserId actor,
        TimeSpan? lifetime = null,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);

        TimeSpan ttl = lifetime ?? _options.DefaultLifetime;
        if (ttl <= TimeSpan.Zero || ttl > _options.MaxLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                $"Enrollment lifetime must be positive and at most {_options.MaxLifetime}.");
        }

        DateTimeOffset now = _clock.GetUtcNow();
        SecretString secret = _hasher.Generate(CredentialPrefixes.Enrollment);
        Enrollment enrollment = Enrollment.Issue(_hasher.Hash(secret), actor, now, now + ttl, label);

        _context.Add(enrollment);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(EnrollmentAuditActions.TokenIssued, AuditOutcome.Succeeded, actor, null, $"enrollment {enrollment.Id}"),
            cancellationToken).ConfigureAwait(false);

        return new EnrollmentTokenResult(enrollment.Id, secret, enrollment.ExpiresAt, label);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnrollmentSummary>> ListAsync(
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Enrollment> all = await _enrollments.ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(e => new EnrollmentSummary(e.Id, e.Status, e.CreatedAt, e.ExpiresAt, e.Label, e.ConsumedByAgent))
            .ToList();
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        UserId actor,
        EnrollmentId enrollmentId,
        CancellationToken cancellationToken = default)
    {
        await RequireEnrollmentManageAsync(actor, cancellationToken).ConfigureAwait(false);

        Enrollment enrollment = await _enrollments.FindByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationDeniedException("Enrollment not found in the current tenant.");

        enrollment.Revoke();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(EnrollmentAuditActions.Revoked, AuditOutcome.Succeeded, actor, null, $"enrollment {enrollment.Id}"),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireEnrollmentManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.TenantEnrollmentManage.Name))
        {
            throw new AuthorizationDeniedException(
                $"{Permissions.TenantEnrollmentManage.Name} is required to manage enrollment.");
        }
    }
}
