using ZWarden.Domain.Ids;

namespace ZWarden.Application.Authentication;

/// <summary>
/// A single authentication-relevant occurrence (PRD 11). It carries <b>only non-secret</b> identifiers
/// and context — the typed <see cref="UserId"/> and <see cref="TenantId"/>, the kind, when it happened,
/// and an optional non-secret <see cref="Detail"/>. It never carries a password, token, TOTP secret, or
/// recovery code, so binding a durable sink (F6) can never start logging credentials.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="UserId">The subject, when known (a failed sign-in for an unknown address has none).</param>
/// <param name="TenantId">The tenant the subject belongs to, when known.</param>
/// <param name="OccurredAt">When the event occurred (UTC).</param>
/// <param name="Detail">An optional short, non-secret description (e.g. "wrong password"); never a credential.</param>
public sealed record AuthenticationEvent(
    AuthenticationEventKind Kind,
    UserId? UserId,
    TenantId? TenantId,
    DateTimeOffset OccurredAt,
    string? Detail = null);
