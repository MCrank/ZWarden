using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Tenancy;

/// <summary>
/// Re-checks an open circuit's session every <see cref="SessionRevalidator.Interval"/> (#297, ADR 0046 Q3): the
/// user still exists, the security stamp still matches, and the user still holds a role. When the check fails,
/// the circuit's user becomes anonymous, so <c>[Authorize]</c> and <c>AuthorizeView</c> close the page within
/// about a minute instead of when the tab closes. Mutating services still check permissions themselves.
/// </summary>
public sealed class SessionRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly SessionRevalidator _revalidator;

    public SessionRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, SessionRevalidator revalidator)
        : base(loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(revalidator);
        _revalidator = revalidator;
    }

    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => SessionRevalidator.Interval;

    /// <inheritdoc />
    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticationState);
        return _revalidator.IsStillValidAsync(authenticationState.User, cancellationToken);
    }
}
