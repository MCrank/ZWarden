using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Audit;

/// <summary>
/// A durable record of a security- or administration-relevant occurrence — the <c>aud-</c> record the
/// prefix registry reserves (PRD 11; ADR 0019). It is <b>append-only</b>: every property is init-only,
/// there is no update or delete path, and it carries no <see cref="IVersioned"/> concurrency token, so a
/// recorded event is the event as written. It is <see cref="ITenantOwned"/>, so the ownership interceptor
/// stamps the ambient tenant on insert and the tenant filter scopes every read (ADR 0016) — a tenant sees
/// only its own audit trail.
/// <para>
/// It carries only <b>non-secret</b> data: a stable machine-readable <see cref="Action"/> (the audit
/// currency, never an ad-hoc string), an <see cref="AuditOutcome"/>, when it happened, and optional
/// non-secret context (the actor, a Server, a correlation id, a short detail). It never holds a credential —
/// exactly as the <c>AuthenticationEvent</c> it is often written from already guarantees.
/// </para>
/// </summary>
public sealed class AuditEvent : ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public AuditEvent()
    {
    }

    /// <summary>The audit event identifier (<c>aud-&lt;uuid&gt;</c>).</summary>
    public AuditEventId Id { get; init; } = AuditEventId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>When the occurrence happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>The stable machine-readable action name, e.g. <c>Authentication.SignInSucceeded</c> or
    /// <c>Role.Created</c>. The audit currency — never a free-form, per-call-site string.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>The outcome of the occurrence.</summary>
    public AuditOutcome Outcome { get; init; }

    /// <summary>The user who caused the occurrence, when known (a failed sign-in for an unknown address
    /// has none).</summary>
    public UserId? ActorUserId { get; init; }

    /// <summary>The Server the occurrence concerns, when it is server-scoped; otherwise <c>null</c>.</summary>
    public ServerId? ServerId { get; init; }

    /// <summary>The ambient correlation id tying related events together (one request/operation), or
    /// <c>null</c> when none is in scope.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>An optional short, non-secret description; never a credential.</summary>
    public string? Detail { get; init; }

    /// <summary>Creates an audit event. The <see cref="TenantId"/> is left unset so the ownership
    /// interceptor stamps the ambient tenant on insert (ADR 0016).</summary>
    public static AuditEvent Create(
        string action,
        AuditOutcome outcome,
        DateTimeOffset occurredAt,
        UserId? actorUserId = null,
        ServerId? serverId = null,
        string? correlationId = null,
        string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new AuditEvent
        {
            Id = AuditEventId.New(),
            Action = action,
            Outcome = outcome,
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            ServerId = serverId,
            CorrelationId = correlationId,
            Detail = detail,
        };
    }
}
