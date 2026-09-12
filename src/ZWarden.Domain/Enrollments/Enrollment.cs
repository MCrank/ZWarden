using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Enrollments;

/// <summary>
/// A <b>one-time, short-lived enrollment credential</b> (PRD 63A; ADR 0007) — the <c>enr-</c> record the
/// prefix registry reserves. An operator with <c>Tenant.Enrollment.Manage</c> mints one; an unknown Agent
/// presents its secret once and exchanges it for a per-Agent credential, which <see cref="Consume"/>s this
/// enrollment. It is <see cref="ITenantOwned"/> (the interceptor stamps the ambient tenant on insert and
/// the filter scopes every read, ADR 0016) and <see cref="IVersioned"/> — the concurrency token is what
/// makes a double-redeem race resolve to exactly one winner.
/// <para>
/// It never holds the raw secret: only a <b>one-way hash</b> of it (decision 2, hash-only). A database
/// compromise yields the hash of a full-entropy secret, which is not a usable credential.
/// </para>
/// </summary>
public sealed class Enrollment : IVersioned, ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public Enrollment()
    {
    }

    /// <summary>The enrollment identifier (<c>enr-&lt;uuid&gt;</c>).</summary>
    public EnrollmentId Id { get; init; } = EnrollmentId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The one-way hash of the enrollment secret; the raw secret is shown once and never stored.</summary>
    public string SecretHash { get; init; } = string.Empty;

    /// <summary>The operator who minted this enrollment.</summary>
    public UserId CreatedBy { get; init; }

    /// <summary>When it was minted (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it stops being redeemable (UTC); short-lived by design (PRD 63A).</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>An optional operator-supplied label carried to the enrolled Agent; never a secret.</summary>
    public string? Label { get; init; }

    /// <summary>The lifecycle state; single-use (see <see cref="EnrollmentStatus"/>).</summary>
    public EnrollmentStatus Status { get; private set; } = EnrollmentStatus.Pending;

    /// <summary>The Agent this enrollment produced, or <c>null</c> until it is consumed.</summary>
    public AgentId? ConsumedByAgent { get; private set; }

    /// <summary>When it was consumed (UTC), or <c>null</c> until then.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>True when it may still be redeemed at <paramref name="now"/> — pending and not yet expired.</summary>
    public bool IsRedeemable(DateTimeOffset now) => Status == EnrollmentStatus.Pending && now < ExpiresAt;

    /// <summary>
    /// Mints a pending enrollment. The <see cref="TenantId"/> is left unset so the ownership interceptor
    /// stamps the ambient tenant on insert (ADR 0016).
    /// </summary>
    public static Enrollment Issue(
        string secretHash,
        UserId createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretHash);
        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Enrollment expiry must be after its creation time.", nameof(expiresAt));
        }

        return new Enrollment
        {
            Id = EnrollmentId.New(),
            SecretHash = secretHash,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            Label = label,
        };
    }

    /// <summary>
    /// Consumes this single-use enrollment, binding the Agent it produced. Fails closed if it is not
    /// redeemable at <paramref name="now"/> (expired, already consumed, or revoked); callers should check
    /// <see cref="IsRedeemable"/> first and treat a failure here as a defensive last line.
    /// </summary>
    public void Consume(AgentId agentId, DateTimeOffset now)
    {
        if (!IsRedeemable(now))
        {
            throw new InvalidOperationException(
                $"Enrollment {Id} is not redeemable (status {Status}).");
        }

        Status = EnrollmentStatus.Consumed;
        ConsumedByAgent = agentId;
        ConsumedAt = now;
    }

    /// <summary>Cancels an unused enrollment. Only a <see cref="EnrollmentStatus.Pending"/> enrollment may be
    /// revoked — a consumed one has already produced an Agent (revoke the Agent's credential instead).</summary>
    public void Revoke()
    {
        if (Status != EnrollmentStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Only a pending enrollment can be revoked; {Id} is {Status}.");
        }

        Status = EnrollmentStatus.Revoked;
    }
}
