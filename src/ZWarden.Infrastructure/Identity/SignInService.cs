using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Authentication;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Password sign-in that emits an <see cref="AuthenticationEvent"/> for every outcome (PRD 11): success,
/// wrong credentials, or lockout. It wraps <see cref="SignInManager{TUser}"/> so the auth surface (the
/// Blazor login page, S11) gets both the sign-in decision and a recorded event from one call.
/// </summary>
public sealed class SignInService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthenticationEventSink _events;
    private readonly TimeProvider _timeProvider;

    public SignInService(
        SignInManager<ApplicationUser> signInManager,
        IAuthenticationEventSink events,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(signInManager);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _signInManager = signInManager;
        _events = events;
        _timeProvider = timeProvider;
    }

    /// <summary>Checks the password (with lockout) and records the outcome. Returns the raw result so the
    /// caller can drive the MFA step or the cookie sign-in.</summary>
    public async Task<SignInResult> PasswordSignInAsync(
        ApplicationUser user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        SignInResult result = await _signInManager
            .CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        (AuthenticationEventKind kind, string? detail) = result switch
        {
            { IsLockedOut: true } => (AuthenticationEventKind.LockedOut, "locked out"),
            { Succeeded: true } => (AuthenticationEventKind.SignInSucceeded, null),
            _ => (AuthenticationEventKind.SignInFailed, "invalid credentials"),
        };

        await _events.RecordAsync(
            new AuthenticationEvent(kind, user.UserId, user.TenantId, _timeProvider.GetUtcNow(), detail),
            cancellationToken).ConfigureAwait(false);

        return result;
    }
}
