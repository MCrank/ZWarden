using Microsoft.Extensions.Logging;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The default <see cref="IAccountNotification"/>: it records that a recovery message <i>would</i> be
/// sent, by user id only, and <b>deliberately never logs the token</b> (it is a single-use credential).
/// It sends nothing — a self-hosted install without an email transport still completes the token
/// lifecycle out-of-band, and a real transport is registered over the same seam later.
/// </summary>
public sealed partial class LoggingAccountNotification : IAccountNotification
{
    private readonly ILogger<LoggingAccountNotification> _logger;

    public LoggingAccountNotification(ILogger<LoggingAccountNotification> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task SendPasswordResetAsync(ApplicationUser user, string resetToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        LogPasswordResetRequested(user.UserId);
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(ApplicationUser user, string confirmationToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        LogEmailConfirmationRequested(user.UserId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Password reset requested for user {UserId}; no transport configured (token withheld from logs).")]
    private partial void LogPasswordResetRequested(UserId userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Email confirmation requested for user {UserId}; no transport configured (token withheld from logs).")]
    private partial void LogEmailConfirmationRequested(UserId userId);
}
