namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The seam through which account-recovery messages leave the system (F4). F4 proves the <i>token
/// lifecycle</i> (generate → deliver → redeem, single-use); it builds no transport. A self-hosted
/// install without email keeps the no-op/logging default (<see cref="LoggingAccountNotification"/>); a
/// later feature registers an email/SMS implementation behind this same interface. The raw token is a
/// secret and is passed only to the delivery implementation, never logged or returned to a caller.
/// </summary>
public interface IAccountNotification
{
    /// <summary>Delivers a password-reset token (typically embedded in a reset link) to the user.</summary>
    Task SendPasswordResetAsync(ApplicationUser user, string resetToken, CancellationToken cancellationToken = default);

    /// <summary>Delivers an email-confirmation token to the user.</summary>
    Task SendEmailConfirmationAsync(ApplicationUser user, string confirmationToken, CancellationToken cancellationToken = default);
}
