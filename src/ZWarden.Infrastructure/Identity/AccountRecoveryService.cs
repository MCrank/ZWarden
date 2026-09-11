using Microsoft.AspNetCore.Identity;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Account recovery (F4): password reset and email confirmation, built on Identity's token providers and
/// delivered through <see cref="IAccountNotification"/>. Tokens are single-use in effect — a successful
/// reset rotates the security stamp the token is bound to, so the same token cannot be replayed. The
/// request methods do not reveal whether an address exists (they behave the same either way).
/// </summary>
public sealed class AccountRecoveryService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAccountNotification _notifications;

    public AccountRecoveryService(UserManager<ApplicationUser> users, IAccountNotification notifications)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(notifications);
        _users = users;
        _notifications = notifications;
    }

    /// <summary>Generates a password-reset token for the address (if it exists) and hands it to the
    /// notification seam. Returns without signalling whether the address was found.</summary>
    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ApplicationUser? user = await _users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            return; // do not reveal non-existence
        }

        string token = await _users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        await _notifications.SendPasswordResetAsync(user, token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resets the password using a token issued by <see cref="RequestPasswordResetAsync"/>.</summary>
    public Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _users.ResetPasswordAsync(user, token, newPassword);
    }

    /// <summary>Generates an email-confirmation token and hands it to the notification seam.</summary>
    public async Task RequestEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        string token = await _users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        await _notifications.SendEmailConfirmationAsync(user, token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Confirms the user's email with a token issued by <see cref="RequestEmailConfirmationAsync"/>.</summary>
    public Task<IdentityResult> ConfirmEmailAsync(ApplicationUser user, string token)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _users.ConfirmEmailAsync(user, token);
    }
}
