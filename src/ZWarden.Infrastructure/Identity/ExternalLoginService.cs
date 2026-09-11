using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Authentication;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Maps an <see cref="ExternalIdentity"/> asserted by an <see cref="IExternalIdentityProvider"/> to a
/// local <see cref="ApplicationUser"/> (F4, ADR 0017): it finds the user already linked to the external
/// (issuer, subject) pair, or provisions a new one under the ambient tenant and links it. This is the
/// Infrastructure half of the seam — it references Identity, which the Application-level seam must not.
/// A repeat sign-in for the same subject resolves to the same local user.
/// </summary>
public sealed class ExternalLoginService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthenticationEventSink _events;
    private readonly TimeProvider _timeProvider;

    public ExternalLoginService(
        UserManager<ApplicationUser> users,
        IAuthenticationEventSink events,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _users = users;
        _events = events;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns the local user linked to <paramref name="identity"/>, provisioning and linking one
    /// (under the ambient tenant, email pre-confirmed by the provider) on first sign-in.</summary>
    public async Task<ApplicationUser> MapToLocalUserAsync(ExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        ApplicationUser? linked = await _users.FindByLoginAsync(identity.Issuer, identity.Subject).ConfigureAwait(false);
        if (linked is not null)
        {
            return linked;
        }

        string userName = identity.Email ?? $"{identity.Subject}@{identity.Issuer}";
        ApplicationUser user = new(userName)
        {
            Email = identity.Email ?? userName,
            EmailConfirmed = true, // the external provider vouches for the address
        };

        IdentityResult created = await _users.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to provision a local user for the external identity: {string.Join(", ", created.Errors.Select(e => e.Code))}.");
        }

        await _users.AddLoginAsync(user, new UserLoginInfo(identity.Issuer, identity.Subject, identity.Issuer))
            .ConfigureAwait(false);

        await _events.RecordAsync(
            new AuthenticationEvent(AuthenticationEventKind.ExternalLoginLinked, user.UserId, user.TenantId, _timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        return user;
    }
}
