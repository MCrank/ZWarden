using Microsoft.Extensions.Logging;
using ZWarden.Application.Audit;
using ZWarden.Application.Authentication;
using ZWarden.Domain.Audit;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// The durable <see cref="IAuthenticationEventSink"/> (F6; ADR 0019). It maps each authentication event F4
/// emits to an <see cref="AuditEvent"/> — <c>Action = "Authentication.&lt;Kind&gt;"</c>, outcome derived from
/// the kind — and appends it through <see cref="IAuditWriter"/>, superseding the logging-only default. In
/// self-hosted v1.0 the ambient tenant resolves even for an unauthenticated request (the default tenant,
/// ADR 0016), so a pre-auth failure still has a tenant home; the hosted tenant-less case is a documented
/// deferred gap (ADR 0019).
/// <para>
/// Per the seam's contract it <b>never throws into the auth path</b>: an audit-write failure is caught and
/// logged as a warning, never a blocked or falsified sign-in. Because <see cref="AuthenticationEvent"/>
/// carries no credential, the persisted record cannot either.
/// </para>
/// </summary>
public sealed partial class AuditAuthenticationEventSink : IAuthenticationEventSink
{
    private readonly IAuditWriter _writer;
    private readonly ILogger<AuditAuthenticationEventSink> _logger;

    public AuditAuthenticationEventSink(IAuditWriter writer, ILogger<AuditAuthenticationEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logger);
        _writer = writer;
        _logger = logger;
    }

    public async Task RecordAsync(AuthenticationEvent authenticationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticationEvent);

        AuditEntry entry = new(
            Action: $"Authentication.{authenticationEvent.Kind}",
            Outcome: MapOutcome(authenticationEvent.Kind),
            ActorUserId: authenticationEvent.UserId,
            ServerId: null,
            Detail: authenticationEvent.Detail);

        try
        {
            await _writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A sink failure must never block a legitimate sign-in (the seam contract) — degrade to a warning.
            LogAuditWriteFailed(ex, authenticationEvent.Kind);
        }
    }

    /// <summary>A failed sign-in or a lockout is a <see cref="AuditOutcome.Failed"/>; every other kind is a
    /// recorded success (a verified MFA, a completed reset, a linked external login).</summary>
    private static AuditOutcome MapOutcome(AuthenticationEventKind kind) => kind switch
    {
        AuthenticationEventKind.SignInFailed => AuditOutcome.Failed,
        AuthenticationEventKind.LockedOut => AuditOutcome.Failed,
        _ => AuditOutcome.Succeeded,
    };

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to record authentication event {Kind} to the audit store")]
    private partial void LogAuditWriteFailed(Exception exception, AuthenticationEventKind kind);
}
