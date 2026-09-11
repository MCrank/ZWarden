namespace ZWarden.Application.Authentication;

/// <summary>
/// The kinds of authentication-relevant events F4 emits (PRD 11). These are <b>not</b> F6 audit records —
/// F4 raises them through <see cref="IAuthenticationEventSink"/> with a logging default; F6 owns the
/// durable, queryable audit store and binds a real sink over the same seam.
/// </summary>
public enum AuthenticationEventKind
{
    /// <summary>A password sign-in succeeded.</summary>
    SignInSucceeded,

    /// <summary>A sign-in was refused because the credentials were wrong.</summary>
    SignInFailed,

    /// <summary>A sign-in was refused because the account is locked out.</summary>
    LockedOut,

    /// <summary>A two-factor (authenticator) code was verified.</summary>
    MfaVerified,

    /// <summary>A password was reset through the recovery flow.</summary>
    PasswordReset,

    /// <summary>An external identity-provider login was linked to a local user.</summary>
    ExternalLoginLinked,
}
