namespace ZWarden.Application.Authentication;

/// <summary>
/// The one way authentication events leave the auth code (PRD 11). F4 emits through this seam with a
/// logging default; F6 binds a durable, queryable sink over the same interface without touching any auth
/// code. The interface lives in Application and references only Domain types — no Identity or external
/// identity-provider dependency (arch rules 2/6).
/// </summary>
public interface IAuthenticationEventSink
{
    /// <summary>Records an authentication event. Implementations must not throw into the auth path over a
    /// sink failure (an unrecordable event must not block a legitimate sign-in).</summary>
    Task RecordAsync(AuthenticationEvent authenticationEvent, CancellationToken cancellationToken = default);
}
